using BaseVariable;

namespace HistoryJuno;

/// <summary>HistoryJuno 在 HistoryVulcan 注册表中的模块身份。</summary>
public sealed class ModuleInfo : ModuleInfoBase
{
    public override string ModuleName => "HistoryJuno";
    public string CommandPrefix => "juno";
    public override string Description => "Sub2API 轻量桌面控制面板";
    public override string Author => "OneHistory";
    public override string Version =>
        typeof(ModuleInfo).Assembly.GetName().Version?.ToString(3) ?? "0.3.0";
    public override Type? MainClassType => null;
}
