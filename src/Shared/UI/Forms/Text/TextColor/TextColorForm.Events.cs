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
        //  控件事件：面板要交给命令的三个出口（确定 / 去图纸选 / 取消）
        // ==================================================================

        private void OnSwatchClicked(int aci)
        {
            if (aci < 0)
            {
                return;
            }

            if (aci == AciColorNames.ByLayerValue)
            {
                _mode = TextColorMode.ByLayer;
            }
            else if (aci == AciColorNames.ByBlockValue)
            {
                _mode = TextColorMode.ByBlock;
            }
            else
            {
                _mode = TextColorMode.Aci;
                _aci = aci;
                _loading = true;
                try
                {
                    _aciBox.Text = aci.ToString(CultureInfo.InvariantCulture);
                }
                finally
                {
                    _loading = false;
                }

                SyncListToAci();
            }

            RefreshState();
        }

        private void OnListSelected(int index)
        {
            int aci = AciColorNames.ListIndexToAci(index);

            if (aci == AciColorNames.ByLayerValue)
            {
                _mode = TextColorMode.ByLayer;
            }
            else if (aci == AciColorNames.ByBlockValue)
            {
                _mode = TextColorMode.ByBlock;
            }
            else
            {
                _mode = TextColorMode.Aci;
                _aci = aci;
                _loading = true;
                try
                {
                    _aciBox.Text = aci.ToString(CultureInfo.InvariantCulture);
                }
                finally
                {
                    _loading = false;
                }
            }

            RefreshState();
        }

        private void OnAciTyped()
        {
            if (_loading)
            {
                return;
            }

            // 只在输入能构成合法索引色时才认账：
            // 用户清空编辑框准备重打的过程中会短暂出现空串，
            // 那时候若把模式切走，光标所在的这一组控件会突然失效。
            string text = _aciBox.Text;
            if (!TextColorContent.IsDigitsOnly(text))
            {
                return;
            }

            int value = TextColorContent.ParseByte(text, _aci);
            if (value < AciColorNames.MinIndexedColor || value > AciColorNames.MaxIndexedColor)
            {
                return;
            }

            _mode = TextColorMode.Aci;
            _aci = value;
            SyncListToAci();
            RefreshState();
        }

        private void OnRgbTyped()
        {
            if (_loading)
            {
                return;
            }

            if (!TextColorContent.IsDigitsOnly(_redBox.Text)
                && !TextColorContent.IsDigitsOnly(_greenBox.Text)
                && !TextColorContent.IsDigitsOnly(_blueBox.Text))
            {
                return;
            }

            _red = TextColorContent.ParseByte(_redBox.Text, _red);
            _green = TextColorContent.ParseByte(_greenBox.Text, _green);
            _blue = TextColorContent.ParseByte(_blueBox.Text, _blue);
            _mode = TextColorMode.TrueColor;
            RefreshState();
        }

        private void Accept()
        {
            TextColorOptions options;
            if (!TryBuildOptions(out options))
            {
                return;
            }

            Options = options;
            Action = TextColorDialogAction.Apply;
            Close();
        }

        /// <summary>
        /// 「① 在图纸中选择要改色的文字」：把面板上的选项先交回命令，
        /// 再由命令关掉面板去框选，选完带着新的计数再弹回来 ——
        /// 去选一次文字，已经调好的颜色不会丢。
        /// </summary>
        private void AcceptPickObjects()
        {
            TextColorOptions options;
            if (!TryBuildOptions(out options))
            {
                return;
            }

            Options = options;
            Action = TextColorDialogAction.PickObjects;
            Close();
        }

        /// <summary>
        /// 把面板上的选择拼成一份选项。色号非法时弹框拦住并返回 false ——
        /// 「确定」与「① 在图纸中选择…」都走这一条，规则保持一致。
        /// </summary>
        private bool TryBuildOptions(out TextColorOptions options)
        {
            options = new TextColorOptions
            {
                Mode = _mode,
                AciIndex = _aci,
                Red = _red,
                Green = _green,
                Blue = _blue,
                StripColorCodes = _stripCheck.Checked,
                IncludeSingleLineText = _singleLineCheck.Checked,
                IncludeMultiLeader = _multiLeaderCheck.Checked,
                IncludeBlockDefinitions = _blockCheck.Checked
            };

            options.Normalize();

            if (options.Mode == TextColorMode.Aci && !options.HasValidAciIndex())
            {
                MessageBox.Show(
                    this,
                    "索引色号必须是 1-255 之间的整数。",
                    "批量修改多行文字颜色",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);

                return false;
            }

            return true;
        }
    }
}
