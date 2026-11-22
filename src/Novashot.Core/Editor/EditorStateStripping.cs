using Novashot.Core.Editor;

namespace Novashot.Core.Editor;

/// <summary>
/// EditorState 的配置派生辅助（G-13/G-14）：winshot 的 export.includeBackground 语义——
/// 关闭时导出"裸截图"（无背景板/padding/阴影/内衬/边框），圆角与裁剪保留。
/// </summary>
public static class EditorStateStripping
{
    /// <summary>
    /// includeBackground=false 时返回去掉背景板的派生状态；true 时原样返回。
    /// </summary>
    public static EditorState StripBackground(EditorState state, bool includeBackground = false)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (includeBackground) return state;
        if (!state.HasBackground && state.Padding == 0 && state.ShadowSize == 0
            && state.InsetSize == 0 && state.BorderWidth == 0)
            return state;

        return state with
        {
            Background = BackgroundKind.None,
            Padding = 0,
            ShadowSize = 0f,
            InsetSize = 0f,
            BorderWidth = 0f,
        };
    }
}
