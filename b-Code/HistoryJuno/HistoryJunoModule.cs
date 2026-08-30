using HistoryVulcan.Core.Modules;

namespace HistoryJuno;

/// <summary>把 Juno 的命令和 Aurora 页面声明接入宿主总线。</summary>
public sealed class HistoryJunoModule : IModuleContextAware
{
    public void Attach(IModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.RegisterCommands(registry => JunoCommandCatalog.Register(registry, context.Bus));
    }
}
