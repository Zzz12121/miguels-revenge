#if UNITY_EDITOR
using System;
using System.Reflection;
using com.IvanMurzak.Unity.MCP;
using UnityEditor;

/// <summary>
/// Unity-MCP 0.93.2 FileLogStorage.Dispose marks storage disposed BEFORE virtual Flush.
/// Use the supported collector API with a corrected storage implementation. Keep this
/// editor-only, version gated, and outside PackageCache so teammates receive the fix.
/// </summary>
[InitializeOnLoad]
public static class McpLogStorageCompatibility
{
    static double nextCheck;
    static readonly FieldInfo StorageField = typeof(UnityLogCollector).GetField("_logStorage", BindingFlags.Instance | BindingFlags.NonPublic);
    static readonly FieldInfo MutexField = typeof(FileLogStorage).GetField("_fileMutex", BindingFlags.Instance | BindingFlags.NonPublic);
    static readonly FieldInfo DisposedField = typeof(FileLogStorage).GetField("_isDisposed", BindingFlags.Instance | BindingFlags.NonPublic);
    static readonly FieldInfo StreamField = typeof(FileLogStorage).GetField("fileWriteStream", BindingFlags.Instance | BindingFlags.NonPublic);

    static McpLogStorageCompatibility()
    {
        EditorApplication.update += EnsureStorage;
    }

    static void EnsureStorage()
    {
        if (UnityMcpPlugin.Version != "0.93.2" || EditorApplication.isCompiling ||
            EditorApplication.timeSinceStartup < nextCheck || !UnityMcpPluginEditor.HasInstance) return;
        nextCheck = EditorApplication.timeSinceStartup + 1;
        var plugin = UnityMcpPluginEditor.Instance;
        // Do not replace a collector supplied by another integration, or force one into existence.
        if (plugin.LogCollector == null || StorageField == null || MutexField == null ||
            DisposedField == null || StreamField == null ||
            StorageField.GetValue(plugin.LogCollector)?.GetType() != typeof(BufferedFileLogStorage)) return;
        // Retire the stock instance in the correct order as well, otherwise migration itself
        // would reintroduce the warning after every domain reload. Reflection is confined
        // to the verified 0.93.2 layout; never change the package cache or mute Unity logs.
        var oldStorage = (BufferedFileLogStorage)StorageField.GetValue(plugin.LogCollector);
        var disposed = DisposedField.GetValue(oldStorage);
        var markDisposed = disposed.GetType().GetMethod("TrySetTrue", Type.EmptyTypes);
        if (markDisposed == null) return;
        lock (MutexField.GetValue(oldStorage))
        {
            oldStorage.Flush();
            markDisposed.Invoke(disposed, null);
            (StreamField.GetValue(oldStorage) as IDisposable)?.Dispose();
            StreamField.SetValue(oldStorage, null);
            GC.SuppressFinalize(oldStorage);
        }
        // Unsubscribe first; Save() on a disposed collector is intentionally a no-op.
        plugin.LogCollector.Dispose();
        plugin.DisposeLogCollector();
        plugin.AddUnityLogCollector(new OrderedBufferedLogStorage());
    }

    public sealed class OrderedBufferedLogStorage : BufferedFileLogStorage
    {
        public OrderedBufferedLogStorage(string directory = null, string fileName = null)
            : base(cacheFilePath: directory, cacheFileName: fileName) { }

        public override void Dispose()
        {
            lock (_fileMutex)
            {
                if (_isDisposed.Value) return;
                try { Flush(); }
                finally
                {
                    _isDisposed.TrySetTrue();
                    fileWriteStream?.Dispose();
                    fileWriteStream = null;
                    GC.SuppressFinalize(this);
                }
            }
        }
    }
}
#endif
