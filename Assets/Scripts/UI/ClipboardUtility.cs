using System;
using System.Threading.Tasks;
using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
using AOT;
#endif

public static class ClipboardUtility
{
#if UNITY_WEBGL && !UNITY_EDITOR
    private static TaskCompletionSource<bool> pendingCopy;
    private static readonly Action<int> copyCompleted = OnCopyCompleted;

    [DllImport("__Internal")]
    private static extern void PenguiseCopyText(string text, Action<int> callback);

    [MonoPInvokeCallback(typeof(Action<int>))]
    private static void OnCopyCompleted(int succeeded)
    {
        var pending = pendingCopy;
        pendingCopy = null;
        if (succeeded == 1)
            pending?.TrySetResult(true);
        else
            pending?.TrySetException(new InvalidOperationException("Clipboard access failed."));
    }
#endif

    public static Task CopyTextAsync(string text)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        if (pendingCopy != null)
            throw new InvalidOperationException("A clipboard operation is already active.");
        pendingCopy = new TaskCompletionSource<bool>();
        var task = pendingCopy.Task;
        PenguiseCopyText(text, copyCompleted);
        return task;
#else
        GUIUtility.systemCopyBuffer = text;
        if (GUIUtility.systemCopyBuffer != text)
            throw new InvalidOperationException("Clipboard access failed.");
        return Task.CompletedTask;
#endif
    }
}
