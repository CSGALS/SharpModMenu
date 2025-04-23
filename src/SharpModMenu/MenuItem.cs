using CSSUniversalMenuAPI;
using CSSUniversalMenuAPI.Extensions;

namespace SharpModMenu;

internal class MenuItem : IMenuItem, IMenuItemSubtitleExtension
{
	public void RaiseSelected()
	{
		Selected?.Invoke(this);
	}

	// IMenuItem
	public required IMenu Menu { get; init; }
	public string Title { get; set; } = string.Empty;
	public bool Enabled { get; set; } = true;
	public object? Context { get; set; }

	public event ItemSelectedAction? Selected;

	// IMenuItemSubtitleExtension
	public string? Subtitle { get; set; } = null;
}
