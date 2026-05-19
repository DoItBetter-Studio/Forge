using System.Drawing;
using System.Windows.Forms;

namespace Glyphborn.Forge.Colors
{
	public class MenuStripColor : ProfessionalColorTable
	{
		// The main background of the dropdown menu
		public override Color ToolStripDropDownBackground => Color.FromArgb(45, 45, 48);

		// The margin on the left where icons live (set to same as background)
		public override Color ImageMarginGradientBegin => Color.FromArgb(45, 45, 48);
		public override Color ImageMarginGradientMiddle => Color.FromArgb(45, 45, 48);
		public override Color ImageMarginGradientEnd => Color.FromArgb(45, 45, 48);

		// The border around the dropdown
		public override Color MenuBorder => Color.Black;

		// The color of the item when you hover over it
		public override Color MenuItemSelected => Color.FromArgb(62, 62, 64);
		public override Color MenuItemSelectedGradientBegin => Color.FromArgb(62, 62, 64);
		public override Color MenuItemSelectedGradientEnd => Color.FromArgb(62, 62, 64);


		// The border of the selection box
		public override Color MenuItemBorder => Color.FromArgb(0, 122, 204);

		// The background of the top-level menu bar when an item is pressed
		public override Color MenuItemPressedGradientBegin => Color.FromArgb(45, 45, 48);
		public override Color MenuItemPressedGradientEnd => Color.FromArgb(45, 45, 48);

		// Makes the horizontal separator lines dark
		public override Color SeparatorDark => Color.Black;
		public override Color SeparatorLight => Color.Transparent;

		public override Color ToolStripBorder => Color.Black;
	}
}
