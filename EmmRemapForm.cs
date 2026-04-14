using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

// WinForms UI for collecting EMM/PMX/PMM remap inputs.
internal sealed class EmmRemapForm : Form
{
    private readonly TextBox _basePmxTextBox;
    private readonly TextBox _emmTextBox;
    private readonly TextBox _modifiedPmxTextBox;
    private readonly TextBox _outputEmmTextBox;
    private readonly TextBox _pmmTextBox;
    private readonly TextBox _outputPmmTextBox;
    private readonly CheckBox _keepOriginalModelPathCheckBox;

    public EmmRemapForm(string currentModelHint)
    {
        Text = "MME EMM再マップ";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(720, 444);

        Font = SystemFonts.MessageBoxFont;

        Label descriptionLabel = new Label();
        descriptionLabel.AutoSize = false;
        descriptionLabel.Location = new Point(12, 12);
        descriptionLabel.Size = new Size(696, 44);
        descriptionLabel.Text = "改造前PMX(任意)が未指定なら現在ロード中モデルを改造前として使用し、指定時はそのPMXを優先します。"
            + Environment.NewLine
            + "各入力欄はファイルのドラッグ＆ドロップにも対応しています。";
        Controls.Add(descriptionLabel);

        Label currentModelLabel = new Label();
        currentModelLabel.AutoSize = false;
        currentModelLabel.Location = new Point(12, 60);
        currentModelLabel.Size = new Size(696, 20);
        currentModelLabel.Text = "現在モデル: " + currentModelHint;
        Controls.Add(currentModelLabel);

        _basePmxTextBox = CreateOpenPathRow("改造前PMX(任意)", 108, "PMX Files (*.pmx)|*.pmx|All Files (*.*)|*.*");
        _emmTextBox = CreateOpenPathRow("入力EMM", 154, "EMM Files (*.emm)|*.emm|All Files (*.*)|*.*");
        _modifiedPmxTextBox = CreateOpenPathRow("改造後PMX", 200, "PMX Files (*.pmx)|*.pmx|All Files (*.*)|*.*");
        _outputEmmTextBox = CreateSavePathRow("出力EMM", 246, "EMM Files (*.emm)|*.emm|All Files (*.*)|*.*");
        _pmmTextBox = CreateOpenPathRow("入力PMM(任意)", 292, "PMM Files (*.pmm)|*.pmm|All Files (*.*)|*.*");
        _outputPmmTextBox = CreateSavePathRow("出力PMM(任意)", 338, "PMM Files (*.pmm)|*.pmm|All Files (*.*)|*.*");

        _keepOriginalModelPathCheckBox = new CheckBox();
        _keepOriginalModelPathCheckBox.Text = "出力内のモデルパスを変更しない";
        _keepOriginalModelPathCheckBox.Location = new Point(24, 380);
        _keepOriginalModelPathCheckBox.Size = new Size(680, 24);
        Controls.Add(_keepOriginalModelPathCheckBox);

        Button runButton = new Button();
        runButton.Text = "実行";
        runButton.Location = new Point(552, 408);
        runButton.Size = new Size(75, 28);
        runButton.Click += delegate
        {
            string validationMessage = ValidateFormInput();
            if (validationMessage.Length > 0)
            {
                MessageBox.Show(validationMessage, "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        };
        Controls.Add(runButton);

        Button cancelButton = new Button();
        cancelButton.Text = "キャンセル";
        cancelButton.Location = new Point(633, 408);
        cancelButton.Size = new Size(75, 28);
        cancelButton.DialogResult = DialogResult.Cancel;
        Controls.Add(cancelButton);

        AcceptButton = runButton;
        CancelButton = cancelButton;

        _emmTextBox.TextChanged += delegate
        {
            SetDefaultOutputPathIfEmpty(_emmTextBox, _outputEmmTextBox, "_remapped.emm");
        };

        _pmmTextBox.TextChanged += delegate
        {
            SetDefaultOutputPathIfEmpty(_pmmTextBox, _outputPmmTextBox, "_remapped.pmm");
        };
    }

    public RemapRequest BuildRemapRequest()
    {
        RemapRequest input = new RemapRequest();
        input.BasePmxPath = _basePmxTextBox.Text.Trim();
        input.EmmPath = _emmTextBox.Text.Trim();
        input.ModifiedPmxPath = _modifiedPmxTextBox.Text.Trim();
        input.OutputEmmPath = _outputEmmTextBox.Text.Trim();
        input.PmmPath = _pmmTextBox.Text.Trim();
        input.OutputPmmPath = _outputPmmTextBox.Text.Trim();
        input.KeepOriginalModelPath = _keepOriginalModelPathCheckBox.Checked;
        return input;
    }

    private string ValidateFormInput()
    {
        string basePmxPath = _basePmxTextBox.Text.Trim();
        if (basePmxPath.Length > 0 && !File.Exists(basePmxPath))
        {
            return "改造前PMXが見つかりません。";
        }

        string emmPath = _emmTextBox.Text.Trim();
        if (emmPath.Length == 0)
        {
            return "入力EMMを指定してください。";
        }

        if (!File.Exists(emmPath))
        {
            return "入力EMMが見つかりません。";
        }

        string modifiedPmxPath = _modifiedPmxTextBox.Text.Trim();
        if (modifiedPmxPath.Length == 0)
        {
            return "改造後PMXを指定してください。";
        }

        if (!File.Exists(modifiedPmxPath))
        {
            return "改造後PMXが見つかりません。";
        }

        if (_outputEmmTextBox.Text.Trim().Length == 0)
        {
            return "出力EMMを指定してください。";
        }

        string pmmPath = _pmmTextBox.Text.Trim();
        string outputPmmPath = _outputPmmTextBox.Text.Trim();
        if (pmmPath.Length > 0 && !File.Exists(pmmPath))
        {
            return "入力PMMが見つかりません。";
        }

        if (pmmPath.Length > 0 && outputPmmPath.Length == 0)
        {
            return "入力PMMを指定した場合は出力PMMも指定してください。";
        }

        return string.Empty;
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

    private TextBox CreateOpenPathRow(string labelText, int top, string filter)
    {
        return CreatePathRow(labelText, top, filter, false);
    }

    private TextBox CreateSavePathRow(string labelText, int top, string filter)
    {
        return CreatePathRow(labelText, top, filter, true);
    }

    private TextBox CreatePathRow(string labelText, int top, string filter, bool saveMode)
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

        return textBox;
    }
}
