// Pipeline eval_file body. Compares the original lookup implementation with the current cache.
UnityEngine.InputSystem.Controls.ButtonControl PreviousResolve(string path)
{
    if (string.IsNullOrEmpty(path)) return null;
    int split = path.IndexOf('/');
    if (split < 0) return null;
    string device = path.Substring(0, split), control = path.Substring(split + 1);
    UnityEngine.InputSystem.InputDevice input = device switch
    {
        "<Keyboard>" => UnityEngine.InputSystem.Keyboard.current,
        "<Mouse>" => UnityEngine.InputSystem.Mouse.current,
        "<Gamepad>" => UnityEngine.InputSystem.Gamepad.current,
        _ => null
    };
    return input?.TryGetChildControl<UnityEngine.InputSystem.Controls.ButtonControl>(control);
}
string path = InputBindings.KeyboardPath(GameAction.Jump);
for (int i = 0; i < 100; i++) { PreviousResolve(path); InputBindings.Resolve(path); }
const int count = 10000;
var stopwatch = new System.Diagnostics.Stopwatch();
long before = System.GC.GetAllocatedBytesForCurrentThread();
stopwatch.Start();
for (int i = 0; i < count; i++) PreviousResolve(path);
stopwatch.Stop();
long originalBytes = System.GC.GetAllocatedBytesForCurrentThread() - before;
double originalMs = stopwatch.Elapsed.TotalMilliseconds;
stopwatch.Reset();
before = System.GC.GetAllocatedBytesForCurrentThread();
stopwatch.Start();
for (int i = 0; i < count; i++) InputBindings.Resolve(path);
stopwatch.Stop();
long cachedBytes = System.GC.GetAllocatedBytesForCurrentThread() - before;
var result = new
{
    calls = count, originalBytes, cachedBytes, originalMs, cachedMs = stopwatch.Elapsed.TotalMilliseconds,
    context = "Warm input lookup microbenchmark in Unity Editor. Does not measure rendered gameplay frame rate."
};
System.IO.Directory.CreateDirectory("output/game-feel");
string json = Newtonsoft.Json.JsonConvert.SerializeObject(result, Newtonsoft.Json.Formatting.Indented);
System.IO.File.WriteAllText("output/game-feel/input-allocation-checks.json", json);
return json;
