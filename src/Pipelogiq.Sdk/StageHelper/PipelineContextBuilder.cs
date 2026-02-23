using System.Text.Json;
using PipelogiqSDK.Abstractions;
using PipelogiqSDK.Contracts;
using PipelogiqSDK.Execution;

namespace PipelogiqSDK.StageHelper;

public static class PipelineContextBuilder
{
    private static readonly Dictionary<int, List<ContextItem>> ItemsByPipeline = new();
    private static readonly ReaderWriterLockSlim DictLock = new();


    public static void AddItem(this IStageContext? context, string key, object value)
    {
        if (context?.PipelineId == null)
            return;

        var item = new ContextItem
        {
            Key = key,
            Value = JsonSerializer.Serialize(value),
            ValueType = value.GetType().AssemblyQualifiedName ?? value.GetType().FullName ?? string.Empty,
        };

        DictLock.EnterUpgradeableReadLock();
        try
        {
            if (!ItemsByPipeline.ContainsKey(context.PipelineId.Value))
            {
                DictLock.EnterWriteLock();
                try
                {
                    ItemsByPipeline[context.PipelineId.Value] = new List<ContextItem>();
                }
                finally
                {
                    DictLock.ExitWriteLock();
                }
            }

            DictLock.EnterWriteLock();
            try
            {
                var items = ItemsByPipeline[context.PipelineId.Value];
                var index = items.FindIndex(x => x.Key == key);
                if (index >= 0)
                {
                    items[index] = item;
                }
                else
                {
                    items.Add(item);
                }
            }
            finally
            {
                DictLock.ExitWriteLock();
            }
        }
        finally
        {
            DictLock.ExitUpgradeableReadLock();
        }
    }
}
