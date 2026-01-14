## Code Reuse Analysis: Why didn't we extend LlmProbeService?

### Existing LlmProbeService (88 lines):
✅ Has HTTP probing with timeout
✅ Has DispatcherTimer for background checks  
✅ Has concurrency protection
✅ Has disposal pattern

### Why we created GenericServiceMonitor instead:

❌ **LlmProbeService is tightly coupled to LlmConnectionManager**
   - Hardcoded to call `LlmConnectionManager.SetConnected(reachable)`
   - No way to route to AnythingLLMMediator

❌ **Single service only**
   - Only monitors one URL at a time
   - Can't handle multiple services (AnythingLLM, SAP, etc.)

❌ **Fixed notification mechanism**
   - Always notifies LlmConnectionManager
   - No configurable notification routing

### Optimal Solution (Refactored):
We could have made LlmProbeService generic:

```csharp
// 40-50 lines instead of 233
public class LlmProbeService<TNotifier> 
{
    private readonly Action<bool, string> _notificationCallback;
    // ... existing timer logic
    
    public LlmProbeService(string endpoint, Action<bool, string> onStatusChanged)
    {
        _notificationCallback = onStatusChanged;
    }
}

// Usage:
var anythingLLMProbe = new LlmProbeService(
    "http://localhost:3001/api/system/status",
    (isAvailable, serviceName) => AnythingLLMMediator.NotifyStatusUpdated(...)
);
```

### Conclusion:
We added ~150-180 unnecessary lines by not refactoring LlmProbeService first.