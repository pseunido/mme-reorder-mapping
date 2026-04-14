using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

// WinForms UI for collecting EMM/PMX/PMM remap inputs.
internal sealed class EmmRemapForm : Form
{
    private sealed class PathRow
    {
        public readonly Label Label;
        public readonly TextBox TextBox;
        public readonly Button Button;

        public PathRow(Label label, TextBox textBox, Button button)
        {
            Label = label;
            TextBox = textBox;
            Button = button;
        }

        public void SetVisible(bool visible)
        {
            Label.Visible = visible;
            TextBox.Visible = visible;
            Button.Visible = visible;
        }

        public void SetTop(int top)
        {
            Label.Location = new Point(12, top + 6);
            TextBox.Location = new Point(136, top);
            Button.Location = new Point(634, top - 1);
        }
    }

    private readonly Label _descriptionLabel;
    private readonly Label _currentModelLabel;
    private readonly PathRow _basePmxRow;
    private readonly PathRow _emmRow;
    private readonly PathRow _modifiedPmxRow;
    private readonly PathRow _outputEmmRow;
    private readonly PathRow _pmmRow;
    private readonly PathRow _outputPmmRow;
    private readonly Label _modeLabel;
    private readonly RadioButton _standardModeRadioButton;
    private readonly RadioButton _detailModeRadioButton;
    private readonly CheckBox _keepOriginalModelPathCheckBox;
    private readonly Button _runButton;
    private readonly Button _cancelButton;
    private bool _isDetailMode;

    public EmmRemapForm(string currentModelHint)
    {
        Text = "MME EMM再マップ";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(720, 368);

        Font = SystemFonts.MessageBoxFont;

        _descriptionLabel = new Label();
        _descriptionLabel.AutoSize = false;
        _descriptionLabel.Location = new Point(12, 12);
        _descriptionLabel.Size = new Size(496, 44);
        _descriptionLabel.Text = "ベースPMX(任意)が未指定なら現在ロード中モデルをベースとして使用します。"
            + Environment.NewLine
            + "標準モードではベースPMMからベースEMMと出力先を自動決定します。";
        Controls.Add(_descriptionLabel);

        _modeLabel = new Label();
        _modeLabel.AutoSize = true;
        _modeLabel.Location = new Point(518, 16);
        _modeLabel.Text = "表示:";
        Controls.Add(_modeLabel);

        _standardModeRadioButton = new RadioButton();
        _standardModeRadioButton.AutoSize = true;
        _standardModeRadioButton.Location = new Point(562, 14);
        _standardModeRadioButton.Text = "標準";
        _standardModeRadioButton.Checked = true;
        _standardModeRadioButton.CheckedChanged += delegate
        {
            if (_standardModeRadioButton.Checked)
            {
                ApplyModeLayout(false);
            }
        };
        Controls.Add(_standardModeRadioButton);

        _detailModeRadioButton = new RadioButton();
        _detailModeRadioButton.AutoSize = true;
        _detailModeRadioButton.Location = new Point(622, 14);
        _detailModeRadioButton.Text = "詳細";
        _detailModeRadioButton.CheckedChanged += delegate
        {
            if (_detailModeRadioButton.Checked)
            {
                ApplyModeLayout(true);
            }
        };
        Controls.Add(_detailModeRadioButton);

        _currentModelLabel = new Label();
        _currentModelLabel.AutoSize = false;
        _currentModelLabel.Location = new Point(12, 60);
        _currentModelLabel.Size = new Size(696, 20);
        _currentModelLabel.Text = "現在のモデル: " + currentModelHint;
        Controls.Add(_currentModelLabel);

        _pmmRow = CreateOpenPathRow("ベースPMM(入力)", 108, "PMM Files (*.pmm)|*.pmm|All Files (*.*)|*.*");
        _emmRow = CreateOpenPathRow("ベースEMM", 154, "EMM Files (*.emm)|*.emm|All Files (*.*)|*.*");
        _basePmxRow = CreateOpenPathRow("ベースPMX(任意)", 200, "PMX Files (*.pmx)|*.pmx|All Files (*.*)|*.*");
        _modifiedPmxRow = CreateOpenPathRow("更新後PMX", 246, "PMX Files (*.pmx)|*.pmx|All Files (*.*)|*.*");
        _outputPmmRow = CreateSavePathRow("出力PMM", 292, "PMM Files (*.pmm)|*.pmm|All Files (*.*)|*.*");
        _outputEmmRow = CreateSavePathRow("出力EMM", 338, "EMM Files (*.emm)|*.emm|All Files (*.*)|*.*");

        _keepOriginalModelPathCheckBox = new CheckBox();
        _keepOriginalModelPathCheckBox.Text = "出力内のモデルパスを変更しない";
        _keepOriginalModelPathCheckBox.Location = new Point(24, 274);
        _keepOriginalModelPathCheckBox.Size = new Size(680, 24);
        Controls.Add(_keepOriginalModelPathCheckBox);

        _runButton = new Button();
        _runButton.Text = "実行";
        _runButton.Location = new Point(552, 328);
        _runButton.Size = new Size(75, 28);
        _runButton.Click += delegate
        {
            EnsureDerivedPathsFromBasePmm(!_isDetailMode);
            string validationMessage = ValidateFormInput();
            if (validationMessage.Length > 0)
            {
                MessageBox.Show(validationMessage, "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        };
        Controls.Add(_runButton);

        _cancelButton = new Button();
        _cancelButton.Text = "キャンセル";
        _cancelButton.Location = new Point(633, 328);
        _cancelButton.Size = new Size(75, 28);
        _cancelButton.DialogResult = DialogResult.Cancel;
        Controls.Add(_cancelButton);

        AcceptButton = _runButton;
        CancelButton = _cancelButton;

        _emmRow.TextBox.TextChanged += delegate
        {
            if (_isDetailMode)
            {
                SetDefaultOutputPathIfEmpty(_emmRow.TextBox, _outputEmmRow.TextBox, "_remapped.emm");
            }
        };

        _pmmRow.TextBox.TextChanged += delegate
        {
            EnsureDerivedPathsFromBasePmm(!_isDetailMode);
        };

        ApplyModeLayout(false);
    }

    public RemapRequest BuildRemapRequest()
    {
        EnsureDerivedPathsFromBasePmm(!_isDetailMode);

        RemapRequest input = new RemapRequest();
        input.BasePmxPath = _basePmxRow.TextBox.Text.Trim();
        input.EmmPath = _emmRow.TextBox.Text.Trim();
        input.ModifiedPmxPath = _modifiedPmxRow.TextBox.Text.Trim();
        input.OutputEmmPath = _outputEmmRow.TextBox.Text.Trim();
        input.PmmPath = _pmmRow.TextBox.Text.Trim();
        input.OutputPmmPath = _outputPmmRow.TextBox.Text.Trim();
        input.KeepOriginalModelPath = _keepOriginalModelPathCheckBox.Checked;
        return input;
    }

    private string ValidateFormInput()
    {
        string basePmxPath = _basePmxRow.TextBox.Text.Trim();
        if (basePmxPath.Length > 0 && !File.Exists(basePmxPath))
        {
            return "ベースPMXが見つかりません。";
        }

        string modifiedPmxPath = _modifiedPmxRow.TextBox.Text.Trim();
        if (modifiedPmxPath.Length == 0)
        {
            return "更新後PMXを指定してください。";
        }

        if (!File.Exists(modifiedPmxPath))
        {
            return "更新後PMXが見つかりません。";
        }

        string pmmPath = _pmmRow.TextBox.Text.Trim();
        if (pmmPath.Length > 0 && !File.Exists(pmmPath))
        {
            return "ベースPMMが見つかりません。";
        }

        string emmPath = _emmRow.TextBox.Text.Trim();
        if (emmPath.Length == 0)
        {
            return _isDetailMode
                ? "ベースPMMまたはベースEMMを指定してください。"
                : "ベースPMMを指定してください。";
        }

        if (!File.Exists(emmPath))
        {
            return "ベースEMMが見つかりません。";
        }

        if (_outputEmmRow.TextBox.Text.Trim().Length == 0)
        {
            return "出力EMMを指定してください。";
        }

        string outputPmmPath = _outputPmmRow.TextBox.Text.Trim();
        if (pmmPath.Length > 0 && outputPmmPath.Length == 0)
        {
            return "ベースPMMを指定した場合は出力PMMも指定してください。";
        }

        return string.Empty;
    }

    private void ApplyModeLayout(bool isDetailMode)
    {
        _isDetailMode = isDetailMode;

        _emmRow.SetVisible(isDetailMode);
        _outputEmmRow.SetVisible(isDetailMode);
        _outputPmmRow.SetVisible(isDetailMode);

        int top = 108;
        _pmmRow.SetTop(top);
        top += 46;

        if (isDetailMode)
        {
            _emmRow.SetTop(top);
            top += 46;
        }

        _basePmxRow.SetTop(top);
        top += 46;

        _modifiedPmxRow.SetTop(top);
        top += 46;

        if (isDetailMode)
        {
            _outputPmmRow.SetTop(top);
            top += 46;

            _outputEmmRow.SetTop(top);
            top += 46;
        }

        _keepOriginalModelPathCheckBox.Location = new Point(24, top);

        int buttonTop = top + 34;
        _runButton.Location = new Point(552, buttonTop);
        _cancelButton.Location = new Point(633, buttonTop);
        ClientSize = new Size(720, buttonTop + 36);

        EnsureDerivedPathsFromBasePmm(!isDetailMode);
    }

    private void EnsureDerivedPathsFromBasePmm(bool forceOverride)
    {
        string pmmPath = _pmmRow.TextBox.Text.Trim();
        if (pmmPath.Length == 0)
        {
            if (forceOverride)
            {
                _emmRow.TextBox.Text = string.Empty;
                _outputEmmRow.TextBox.Text = string.Empty;
                _outputPmmRow.TextBox.Text = string.Empty;
            }

            return;
        }

        if (pmmPath.Length > 0)
        {
            string derivedEmmPath = Path.ChangeExtension(pmmPath, ".emm");
            if (forceOverride || _emmRow.TextBox.Text.Trim().Length == 0)
            {
                _emmRow.TextBox.Text = derivedEmmPath;
            }

            if (forceOverride || _outputPmmRow.TextBox.Text.Trim().Length == 0)
            {
                _outputPmmRow.TextBox.Text = BuildRemappedPath(pmmPath, ".pmm");
            }
        }

        string emmPath = _emmRow.TextBox.Text.Trim();
        if (emmPath.Length > 0 && (forceOverride || _outputEmmRow.TextBox.Text.Trim().Length == 0))
        {
            _outputEmmRow.TextBox.Text = BuildRemappedPath(emmPath, ".emm");
        }
    }

    private static string BuildRemappedPath(string inputPath, string extension)
    {
        string directory = Path.GetDirectoryName(inputPath);
        string nameWithoutExtension = Path.GetFileNameWithoutExtension(inputPath);
        if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(nameWithoutExtension))
        {
            return string.Empty;
        }

        return Path.Combine(directory, nameWithoutExtension + "_remapped" + extension);
    }

    private static void SetDefaultOutputPathIfEmpty(TextBox inputTextBox, TextBox outputTextBox, string suffix)
    {
        if (outputTextBox.Text.Trim().Length > 0)
        {
            return;
        }

        string inputPath = inputTextBox.Text.Trim();
        if (inputPath.Length == 0)
        {
            return;
        }

        string directory = Path.GetDirectoryName(inputPath);
        string nameWithoutExtension = Path.GetFileNameWithoutExtension(inputPath);
        if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(nameWithoutExtension))
        {
            return;
        }

        outputTextBox.Text = Path.Combine(directory, nameWithoutExtension + suffix);
    }

    private PathRow CreateOpenPathRow(string labelText, int top, string filter)
    {
        return CreatePathRow(labelText, top, filter, false);
    }

    private PathRow CreateSavePathRow(string labelText, int top, string filter)
    {
        return CreatePathRow(labelText, top, filter, true);
    }

    private PathRow CreatePathRow(string labelText, int top, string filter, bool saveMode)
    {
        Label label = new Label();
        label.AutoSize = true;
        label.Location = new Point(12, top + 6);
        label.Text = labelText;
        Controls.Add(label);

        TextBox textBox = new TextBox();
        textBox.Location = new Point(136, top);
        textBox.Size = new Size(492, 23);
        textBox.AllowDrop = true;
        textBox.DragEnter += delegate (object sender, DragEventArgs e)
        {
            if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effect = DragDropEffects.Copy;
            }
            else
            {
                e.Effect = DragDropEffects.None;
            }
        };
        textBox.DragDrop += delegate (object sender, DragEventArgs e)
        {
            if (e.Data == null || !e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                return;
            }

            string[] dropped = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (dropped == null || dropped.Length == 0)
            {
                return;
            }

            string path = dropped[0];
            if (!saveMode && Directory.Exists(path))
            {
                return;
            }

            textBox.Text = path;
        };
        Controls.Add(textBox);

        Button button = new Button();
        button.Text = "参照...";
        button.Location = new Point(634, top - 1);
        button.Size = new Size(74, 26);
        button.Click += delegate
        {
            if (saveMode)
            {
                using (SaveFileDialog dialog = new SaveFileDialog())
                {
                    dialog.Filter = filter;
                    dialog.OverwritePrompt = true;
                    dialog.AddExtension = true;
                    if (textBox.Text.Trim().Length > 0)
                    {
                        dialog.FileName = Path.GetFileName(textBox.Text.Trim());
                        string directory = Path.GetDirectoryName(textBox.Text.Trim());
                        if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
                        {
                            dialog.InitialDirectory = directory;
                        }
                    }

                    if (dialog.ShowDialog() == DialogResult.OK)
                    {
                        textBox.Text = dialog.FileName;
                    }
                }
            }
            else
            {
                using (OpenFileDialog dialog = new OpenFileDialog())
                {
                    dialog.Filter = filter;
                    dialog.CheckFileExists = true;
                    dialog.Multiselect = false;
                    if (textBox.Text.Trim().Length > 0)
                    {
                        dialog.FileName = Path.GetFileName(textBox.Text.Trim());
                        string directory = Path.GetDirectoryName(textBox.Text.Trim());
                        if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
                        {
                            dialog.InitialDirectory = directory;
                        }
                    }

                    if (dialog.ShowDialog() == DialogResult.OK)
                    {
                        textBox.Text = dialog.FileName;
                    }
                }
            }
        };
        Controls.Add(button);

        return new PathRow(label, textBox, button);
    }
}
