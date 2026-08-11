using System;
using System.Threading;
using System.Threading.Tasks;

namespace MateEngine.AIVoiceMod
{
    internal static class BackgroundTask
    {
        public static Task Run(Action action, CancellationToken token)
        {
            return Run<int>(() => { action(); return 0; }, token);
        }

        public static Task<T> Run<T>(Func<T> action, CancellationToken token)
        {
            var completion = new TaskCompletionSource<T>();
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    token.ThrowIfCancellationRequested();
                    completion.TrySetResult(action());
                }
                catch (OperationCanceledException) { completion.TrySetCanceled(); }
                catch (Exception ex) { completion.TrySetException(ex); }
            });
            return completion.Task;
        }
    }
}
