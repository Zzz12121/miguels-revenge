// Editor-only, outside Assets. Compile/run with unity run_script, entry DojoGlbExport.Run.
// Dependency already present: com.unity.nuget.newtonsoft-json (3.2.2 in packages-lock).
// No asset imports, source mesh/material changes, texture re-encoding, or scene changes.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

public static class DojoGlbExport
{
    public static string Run()
    {
        GameObject arena = GameObject.Find("FinalDojoArena");
        if (arena == null) throw new InvalidOperationException("FinalDojoArena was not found.");
        Transform dojo = arena.transform.Find("Architecture/MainDojo");
        if (dojo == null) throw new InvalidOperationException("Architecture/MainDojo was not found.");
        var exports = new JArray(
            JObject.Parse(Export(arena, "output/dojo-building/FinalDojoArena.glb")),
            JObject.Parse(Export(dojo.gameObject, "output/dojo-building/MainDojo_Complete.glb")));
        var building = new GameObject("DojoBuildings_Complete");
        try
        {
            foreach (string name in new[] { "MainDojo", "WestWeaponsAnnex", "EastTeaAnnex" })
            {
                Transform source = arena.transform.Find("Architecture/" + name);
                if (source == null) throw new InvalidOperationException("Missing building: " + name);
                var clone = UnityEngine.Object.Instantiate(source.gameObject, building.transform);
                clone.name = name;
                clone.transform.localPosition = source.position - dojo.position;
                clone.transform.localRotation = source.rotation;
                clone.transform.localScale = source.lossyScale;
            }
            exports.Add(JObject.Parse(Export(building, "output/dojo-building/DojoBuildings_Complete.glb")));
        }
        finally { UnityEngine.Object.DestroyImmediate(building); }
        return new JObject { ["exports"] = exports }.ToString(Formatting.None);
    }

    /// <summary>
    /// Export active, enabled static MeshFilter/MeshRenderer geometry beneath root.
    /// Vertices are baked into root-local space: root placement/rotation/scale are omitted.
    /// Subtree transforms (including nonuniform/mirrored scale) are retained in geometry.
    /// Preview/Environment and camera/light subtrees are excluded, including the root.
    /// Only triangle submeshes are supported; unreadable meshes fail without reimporting.
    /// Returns a JSON summary; writes only the requested GLB, relative to project root.
    /// </summary>
    public static string Export(GameObject root, string path)
    {
        if (root == null) throw new ArgumentNullException(nameof(root));
        string project = Directory.GetParent(Application.dataPath).FullName;
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("GLB path is empty.");
        string fullPath = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(project, path));
        if (!string.Equals(Path.GetExtension(fullPath), ".glb", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Output must have a .glb extension.");
        foreach (string protectedDirectory in new[] { "Assets", "Packages", "Library", "ProjectSettings" })
        {
            string prefix = Path.Combine(project, protectedDirectory) + Path.DirectorySeparatorChar;
            if (fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Export outside Unity project asset/settings/cache directories.");
        }
        using (var exporter = new Writer(root)) return exporter.Save(fullPath);
    }

    sealed class Writer : IDisposable
    {
        readonly GameObject root;
        readonly MemoryStream bin = new MemoryStream();
        readonly BinaryWriter binary;
        readonly JArray views = new JArray(), accessors = new JArray(), meshes = new JArray();
        readonly JArray nodes = new JArray(), materials = new JArray(), images = new JArray();
        readonly JArray textures = new JArray(), samplers = new JArray(), children = new JArray();
        readonly Dictionary<Material, int> materialIds = new Dictionary<Material, int>();
        readonly Dictionary<string, int> imageIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<Texture, int> textureIds = new Dictionary<Texture, int>();
        readonly Dictionary<string, int> samplerIds = new Dictionary<string, int>();
        readonly HashSet<string> warnings = new HashSet<string>();
        bool emissiveStrength;
        int defaultMaterial = -1;
        long triangles;
        int primitiveCount;

        public Writer(GameObject root)
        {
            this.root = root;
            binary = new BinaryWriter(bin, Encoding.UTF8, true);
            nodes.Add(new JObject { ["name"] = root.name, ["children"] = children });
        }

        public string Save(string path)
        {
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(false))
            {
                MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
                if (renderer == null || !renderer.enabled || Excluded(filter.transform)) continue;
                if (filter.sharedMesh != null) AddMesh(filter, renderer);
            }
            if (meshes.Count == 0) throw new InvalidOperationException("No eligible architecture triangle meshes under " + root.name);
            Align();
            var document = new JObject
            {
                ["asset"] = new JObject { ["version"] = "2.0", ["generator"] = "DojoGlbExport" },
                ["scene"] = 0,
                ["scenes"] = new JArray(new JObject { ["name"] = root.name, ["nodes"] = new JArray(0) }),
                ["nodes"] = nodes, ["meshes"] = meshes, ["materials"] = materials,
                ["accessors"] = accessors, ["bufferViews"] = views,
                ["buffers"] = new JArray(new JObject { ["byteLength"] = bin.Length })
            };
            if (images.Count > 0)
            {
                document["images"] = images;
                document["textures"] = textures;
                document["samplers"] = samplers;
            }
            if (emissiveStrength) document["extensionsUsed"] = new JArray("KHR_materials_emissive_strength");
            byte[] json = Encoding.UTF8.GetBytes(document.ToString(Formatting.None));
            int paddedJson = checked((json.Length + 3) & ~3);
            uint total = checked((uint)(12L + 8 + paddedJson + 8 + bin.Length));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (var output = new BinaryWriter(File.Open(path, FileMode.Create, FileAccess.Write)))
            {
                output.Write(0x46546C67u); output.Write(2u); output.Write(total);
                output.Write((uint)paddedJson); output.Write(0x4E4F534Au); output.Write(json);
                for (int i = json.Length; i < paddedJson; ++i) output.Write((byte)0x20);
                output.Write(checked((uint)bin.Length)); output.Write(0x004E4942u);
                bin.Position = 0;
                bin.CopyTo(output.BaseStream);
            }
            var warningArray = new JArray();
            foreach (string warning in warnings) warningArray.Add(warning);
            return new JObject
            {
                ["root"] = root.name, ["path"] = path, ["triangles"] = triangles,
                ["meshes"] = meshes.Count, ["primitives"] = primitiveCount,
                ["materials"] = materials.Count, ["images"] = images.Count,
                ["bytes"] = total, ["warnings"] = warningArray
            }.ToString(Formatting.None);
        }

        bool Excluded(Transform transform)
        {
            for (Transform t = transform; t != null; t = t.parent)
            {
                if (string.Equals(t.name, "Preview", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(t.name, "Environment", StringComparison.OrdinalIgnoreCase)
                    || t.GetComponent<Camera>() != null || t.GetComponent<Light>() != null) return true;
                if (t == root.transform) break;
            }
            return false;
        }

        void AddMesh(MeshFilter filter, MeshRenderer renderer)
        {
            Mesh source = filter.sharedMesh;
            if (!source.isReadable)
                throw new InvalidOperationException(source.name + " is not readable; provide a readable mesh before exporting.");
            if (source.vertexCount == 0 || source.subMeshCount == 0) return;
            Matrix4x4 matrix = root.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            float determinant = matrix.determinant;
            if (!Finite(determinant) || Mathf.Abs(determinant) < 1e-12f)
                throw new InvalidOperationException("Singular/nonfinite transform: " + filter.name);
            Matrix4x4 normalMatrix = matrix.inverse.transpose;
            Vector3[] vertices = source.vertices, normals = source.normals;
            Vector2[] uv = source.uv;
            var submeshes = new List<int[]>();
            for (int s = 0; s < source.subMeshCount; ++s)
            {
                var descriptor = source.GetSubMesh(s);
                if (descriptor.topology != MeshTopology.Triangles)
                    throw new NotSupportedException(source.name + ": only triangle submeshes are supported.");
                // applyBaseVertex=true handles imported submeshes with nonzero baseVertex.
                int[] indices = source.GetIndices(s, true);
                if (indices.Length % 3 != 0) throw new InvalidDataException("Incomplete triangle in " + source.name);
                foreach (int index in indices)
                    if (index < 0 || index >= vertices.Length) throw new InvalidDataException("Mesh index out of range.");
                submeshes.Add(indices);
            }
            if (normals.Length != vertices.Length)
            {
                normals = new Vector3[vertices.Length];
                foreach (int[] indices in submeshes)
                    for (int i = 0; i < indices.Length; i += 3)
                    {
                        int a = indices[i], b = indices[i + 1], c = indices[i + 2];
                        Vector3 n = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
                        normals[a] += n; normals[b] += n; normals[c] += n;
                    }
                warnings.Add(source.name + ": missing normals generated in memory (source unchanged).");
            }
            for (int i = 0; i < vertices.Length; ++i)
            {
                Vector3 p = matrix.MultiplyPoint3x4(vertices[i]);
                Vector3 n = normalMatrix.MultiplyVector(normals[i]);
                if (!Finite(p.x) || !Finite(p.y) || !Finite(p.z)
                    || !Finite(n.x) || !Finite(n.y) || !Finite(n.z))
                    throw new InvalidDataException("Nonfinite vertex/normal in " + source.name);
                n = n.sqrMagnitude > 1e-20f ? n.normalized : Vector3.up;
                vertices[i] = new Vector3(p.x, p.y, -p.z);
                normals[i] = new Vector3(n.x, n.y, -n.z);
            }
            int positionAccessor = Vectors(vertices, true), normalAccessor = Vectors(normals, false);
            var primitives = new JArray();
            Material[] shared = renderer.sharedMaterials;
            var uvAccessors = new Dictionary<int, int>();
            for (int s = 0; s < submeshes.Count; ++s)
            {
                int[] indices = submeshes[s];
                if (indices.Length == 0) continue;
                Material material = s < shared.Length ? shared[s] : null;
                int materialId = MaterialIndex(material);
                int uvAccessor;
                if (!uvAccessors.TryGetValue(materialId, out uvAccessor))
                {
                    string map = BaseMap(material);
                    Vector2 scale = map != null ? material.GetTextureScale(map) : Vector2.one;
                    Vector2 offset = map != null ? material.GetTextureOffset(map) : Vector2.zero;
                    if (uv.Length != vertices.Length && HasColorTexture(material))
                        throw new InvalidDataException(source.name + ": textured mesh requires UV0.");
                    // URP Lit uses the BaseMap-transformed UV for both base and emission.
                    // Apply ST in Unity coordinates BEFORE v -> 1-v; shared materials stay shared.
                    Align(); long start = bin.Position;
                    for (int i = 0; i < vertices.Length; ++i)
                    {
                        Vector2 value = uv.Length == vertices.Length ? uv[i] : Vector2.zero;
                        Write(value.x * scale.x + offset.x);
                        Write(1f - (value.y * scale.y + offset.y));
                    }
                    uvAccessor = Accessor(View(start, 34962), 5126, vertices.Length, "VEC2");
                    uvAccessors.Add(materialId, uvAccessor);
                }
                // Z reflection reverses winding; a mirrored object introduces a second reversal.
                bool reverse = determinant > 0f;
                Align(); long indexStart = bin.Position;
                for (int i = 0; i < indices.Length; i += 3)
                {
                    binary.Write((uint)indices[i]);
                    binary.Write((uint)indices[i + (reverse ? 2 : 1)]);
                    binary.Write((uint)indices[i + (reverse ? 1 : 2)]);
                }
                int indexAccessor = Accessor(View(indexStart, 34963), 5125, indices.Length, "SCALAR");
                primitives.Add(new JObject
                {
                    ["attributes"] = new JObject
                    {
                        ["POSITION"] = positionAccessor, ["NORMAL"] = normalAccessor, ["TEXCOORD_0"] = uvAccessor
                    },
                    ["indices"] = indexAccessor, ["material"] = materialId, ["mode"] = 4
                });
                triangles += indices.Length / 3;
                ++primitiveCount;
            }
            if (primitives.Count == 0) return;
            int meshId = meshes.Count;
            meshes.Add(new JObject { ["name"] = source.name, ["primitives"] = primitives });
            children.Add(nodes.Count);
            nodes.Add(new JObject { ["name"] = AnimationUtility.CalculateTransformPath(filter.transform, root.transform), ["mesh"] = meshId });
        }

        static string BaseMap(Material material)
        {
            if (material == null) return null;
            return material.HasProperty("_BaseMap") ? "_BaseMap" : material.HasProperty("_MainTex") ? "_MainTex" : null;
        }

        static bool HasColorTexture(Material material)
        {
            if (material == null) return false;
            string map = BaseMap(material);
            return (map != null && material.GetTexture(map) != null)
                || (material.IsKeywordEnabled("_EMISSION") && material.HasProperty("_EmissionMap") && material.GetTexture("_EmissionMap") != null);
        }

        int MaterialIndex(Material material)
        {
            int id;
            if (material != null && materialIds.TryGetValue(material, out id)) return id;
            if (material == null && defaultMaterial >= 0) return defaultMaterial;
            Color baseColor = Color.white;
            if (material != null)
            {
                string property = material.HasProperty("_BaseColor") ? "_BaseColor" : material.HasProperty("_Color") ? "_Color" : null;
                if (property != null) baseColor = material.GetColor(property);
                if (material.HasProperty("_Surface") && material.GetFloat("_Surface") != 0f)
                    throw new NotSupportedException(material.name + ": this exporter expects opaque materials.");
            }
            // Unity ordinary Color properties store authored sRGB; glTF factors are linear.
            // URP HDR _EmissionColor is already linear: do NOT apply .linear a second time.
            // Embedded base/emission PNG/JPEG bytes remain sRGB, decoded by the glTF viewer.
            Color linear = baseColor.linear;
            float smoothness = material == null ? 0f : material.HasProperty("_Smoothness") ? material.GetFloat("_Smoothness")
                : material.HasProperty("_Glossiness") ? material.GetFloat("_Glossiness") : 0f;
            var pbr = new JObject
            {
                ["baseColorFactor"] = new JArray(Unit(linear.r), Unit(linear.g), Unit(linear.b), Unit(baseColor.a)),
                ["metallicFactor"] = 0f, ["roughnessFactor"] = 1f - Unit(smoothness)
            };
            var result = new JObject
            {
                ["name"] = material != null ? material.name : "Default",
                ["pbrMetallicRoughness"] = pbr, ["alphaMode"] = "OPAQUE",
                ["doubleSided"] = material != null && material.HasProperty("_Cull") && Mathf.Approximately(material.GetFloat("_Cull"), 0f)
            };
            string map = BaseMap(material);
            if (map != null && material.GetTexture(map) != null)
                pbr["baseColorTexture"] = new JObject { ["index"] = TextureIndex(material.GetTexture(map)) };
            if (material != null && material.IsKeywordEnabled("_EMISSION"))
            {
                Color e = material.HasProperty("_EmissionColor") ? material.GetColor("_EmissionColor") : Color.black;
                float r = Positive(e.r), g = Positive(e.g), b = Positive(e.b);
                float strength = Mathf.Max(1f, Mathf.Max(r, Mathf.Max(g, b)));
                result["emissiveFactor"] = new JArray(r / strength, g / strength, b / strength);
                if (strength > 1f)
                {
                    emissiveStrength = true;
                    result["extensions"] = new JObject
                    {
                        ["KHR_materials_emissive_strength"] = new JObject { ["emissiveStrength"] = strength }
                    };
                }
                if (material.HasProperty("_EmissionMap") && material.GetTexture("_EmissionMap") != null)
                    result["emissiveTexture"] = new JObject { ["index"] = TextureIndex(material.GetTexture("_EmissionMap")) };
            }
            // Unity metallic/smoothness packed maps cannot be reused as glTF roughness maps.
            // This scope uses scalar roughness and metallic=0; never rewrite source images.
            if (material != null && ((material.HasProperty("_MetallicGlossMap") && material.GetTexture("_MetallicGlossMap") != null)
                || material.IsKeywordEnabled("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A")))
                warnings.Add(material.name + ": packed smoothness map omitted; scalar smoothness used.");
            id = materials.Count;
            materials.Add(result);
            if (material != null) materialIds.Add(material, id);
            else defaultMaterial = id;
            return id;
        }

        int TextureIndex(Texture texture)
        {
            if (!(texture is Texture2D)) throw new NotSupportedException("Only project PNG/JPEG Texture2D assets are supported.");
            int id;
            if (textureIds.TryGetValue(texture, out id)) return id;
            string assetPath = AssetDatabase.GetAssetPath(texture);
            string project = Directory.GetParent(Application.dataPath).FullName;
            string fullPath = Path.GetFullPath(Path.Combine(project, assetPath));
            string extension = Path.GetExtension(fullPath).ToLowerInvariant();
            if (string.IsNullOrEmpty(assetPath) || (extension != ".png" && extension != ".jpg" && extension != ".jpeg") || !File.Exists(fullPath))
                throw new InvalidDataException("Texture requires an existing PNG/JPEG project asset: " + texture.name);
            int image;
            if (!imageIds.TryGetValue(fullPath, out image))
            {
                byte[] bytes = File.ReadAllBytes(fullPath);
                bool png = bytes.Length >= 8 && bytes[0] == 137 && bytes[1] == 80 && bytes[2] == 78 && bytes[3] == 71
                    && bytes[4] == 13 && bytes[5] == 10 && bytes[6] == 26 && bytes[7] == 10;
                bool jpeg = bytes.Length >= 3 && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255;
                if ((extension == ".png" && !png) || (extension != ".png" && !jpeg))
                    throw new InvalidDataException("Texture file signature is not PNG/JPEG: " + assetPath);
                Align(); long start = bin.Position; binary.Write(bytes);
                image = images.Count;
                images.Add(new JObject { ["name"] = texture.name, ["bufferView"] = View(start, 0), ["mimeType"] = png ? "image/png" : "image/jpeg" });
                imageIds.Add(fullPath, image);
                var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
                if (importer != null && !importer.sRGBTexture)
                    warnings.Add(assetPath + ": source imported linear, but glTF base/emission textures are sRGB; bytes preserved.");
            }
            int wrapS = Wrap(texture.wrapModeU), wrapT = Wrap(texture.wrapModeV);
            int mag = texture.filterMode == FilterMode.Point ? 9728 : 9729;
            // No mip dependency: portable even for non-power-of-two source images.
            string samplerKey = wrapS + ":" + wrapT + ":" + mag;
            int sampler;
            if (!samplerIds.TryGetValue(samplerKey, out sampler))
            {
                sampler = samplers.Count;
                samplers.Add(new JObject { ["wrapS"] = wrapS, ["wrapT"] = wrapT, ["magFilter"] = mag, ["minFilter"] = mag });
                samplerIds.Add(samplerKey, sampler);
            }
            id = textures.Count;
            textures.Add(new JObject { ["source"] = image, ["sampler"] = sampler });
            textureIds.Add(texture, id);
            return id;
        }

        int Wrap(TextureWrapMode wrap)
        {
            if (wrap == TextureWrapMode.Repeat) return 10497;
            if (wrap == TextureWrapMode.Mirror) return 33648;
            if (wrap == TextureWrapMode.MirrorOnce) warnings.Add("MirrorOnce wrap approximated as CLAMP_TO_EDGE.");
            return 33071;
        }

        int Vectors(Vector3[] values, bool bounds)
        {
            Align(); long start = bin.Position;
            Vector3 min = values[0], max = values[0];
            foreach (Vector3 value in values)
            {
                Write(value.x); Write(value.y); Write(value.z);
                min = Vector3.Min(min, value); max = Vector3.Max(max, value);
            }
            int accessor = Accessor(View(start, 34962), 5126, values.Length, "VEC3");
            if (bounds)
            {
                accessors[accessor]["min"] = new JArray(min.x, min.y, min.z);
                accessors[accessor]["max"] = new JArray(max.x, max.y, max.z);
            }
            return accessor;
        }

        int View(long start, int target)
        {
            var view = new JObject { ["buffer"] = 0, ["byteOffset"] = start, ["byteLength"] = bin.Position - start };
            if (target != 0) view["target"] = target;
            int id = views.Count; views.Add(view); return id;
        }

        int Accessor(int view, int component, int count, string type)
        {
            int id = accessors.Count;
            accessors.Add(new JObject { ["bufferView"] = view, ["byteOffset"] = 0, ["componentType"] = component, ["count"] = count, ["type"] = type });
            return id;
        }

        static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
        static float Positive(float value)
        {
            if (!Finite(value)) throw new InvalidDataException("Nonfinite material value.");
            return Mathf.Max(0f, value);
        }
        static float Unit(float value) { return Mathf.Min(1f, Positive(value)); }
        void Write(float value)
        {
            if (!Finite(value)) throw new InvalidDataException("Nonfinite accessor value.");
            binary.Write(value);
        }
        void Align() { while ((bin.Position & 3) != 0) binary.Write((byte)0); }
        public void Dispose() { binary.Dispose(); bin.Dispose(); }
    }
}
