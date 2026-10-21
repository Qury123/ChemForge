using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using ChemForge.Core.Text.TextColor;
using ChemForge.Models.Text.TextColor;
using AcColor = Autodesk.AutoCAD.Colors.Color;
using AcColorMethod = Autodesk.AutoCAD.Colors.ColorMethod;
using static ChemForge.UI.Forms.Common.DialogStyle;

namespace ChemForge.UI.Forms.Text.TextColor
{
    public sealed partial class TextColorForm
    {
        // ==================================================================
        //  状态：把选项读进控件、把控件状态写回选项
        // ==================================================================

        private void LoadFrom(TextColorOptions options)
        {
            if (options != null)
            {
                _mode = options.Mode;
                _aci = options.AciIndex;
                _red = options.Red;
                _green = options.Green;
                _blue = options.Blue;
            }

            _loading = true;
            try
            {
                _aciBox.Text = _aci.ToString(CultureInfo.InvariantCulture);
                _redBox.Text = _red.ToString(CultureInfo.InvariantCulture);
                _greenBox.Text = _green.ToString(CultureInfo.InvariantCulture);
                _blueBox.Text = _blue.ToString(CultureInfo.InvariantCulture);

                _stripCheck.Checked = options == null || options.StripColorCodes;
                _singleLineCheck.Checked = options == null || options.IncludeSingleLineText;
                _multiLeaderCheck.Checked = options != null && options.IncludeMultiLeader;
                _blockCheck.Checked = options != null && options.IncludeBlockDefinitions;
            }
            finally
            {
                _loading = false;
            }

            SyncListToAci();
        }

        /// <summary>把列表光标挪到当前色号上。</summary>
        private void SyncListToAci()
        {
            if (_mode != TextColorMode.Aci
                || _aci < AciColorNames.MinIndexedColor
                || _aci > AciColorNames.MaxIndexedColor)
            {
                return;
            }

            _loading = true;
            try
            {
                _colorList.SelectedIndex = AciColorNames.AciToListIndex(_aci);
            }
            finally
            {
                _loading = false;
            }
        }

        private void RefreshState()
        {
            string mode;
            switch (_mode)
            {
                case TextColorMode.Aci:
                    mode = "索引色 " + _aci.ToString(CultureInfo.InvariantCulture)
                        + "（" + AciColorNames.Name(_aci) + "）";
                    break;
                case TextColorMode.TrueColor:
                    mode = "真彩色 RGB(" + _red.ToString(CultureInfo.InvariantCulture)
                        + "," + _green.ToString(CultureInfo.InvariantCulture)
                        + "," + _blue.ToString(CultureInfo.InvariantCulture) + ")";
                    break;
                case TextColorMode.ByLayer:
                    mode = "随层 ByLayer";
                    break;
                default:
                    mode = "随块 ByBlock";
                    break;
            }

            _modeLabel.Text = "当前选择：" + mode;

            foreach (Button button in _swatches)
            {
                int aci = (int)button.Tag;
                bool selected = _mode == TextColorMode.Aci && aci == _aci
                    || _mode == TextColorMode.ByBlock && aci == AciColorNames.ByBlockValue
                    || _mode == TextColorMode.ByLayer && aci == AciColorNames.ByLayerValue;

                button.FlatAppearance.BorderSize = selected ? 3 : 1;
                button.FlatAppearance.BorderColor = selected ? Color.DodgerBlue : Color.Gray;
            }
        }

    }
}
