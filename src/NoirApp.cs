using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Shell;

namespace Noir
{
    public static class Program
    {
        [STAThread]
        public static void Main()
        {
            var application = new Application
            {
                ShutdownMode = ShutdownMode.OnMainWindowClose
            };
            application.Run(new MainWindow());
        }
    }

    internal sealed class MainWindow : Window
    {
        private enum Mode
        {
            Encrypt,
            Decrypt
        }

        private static readonly SolidColorBrush Ink = FrozenBrush(Colors.Black);
        private static readonly SolidColorBrush Paper = FrozenBrush(Colors.White);
        private static readonly FontFamily UiFont = new FontFamily("Microsoft YaHei UI");
        private static readonly FontFamily MonoFont = new FontFamily("Consolas");
        private static readonly FontFamily BrandFont = new FontFamily("Bahnschrift");
        private static readonly FontFamily SymbolFont = new FontFamily("Segoe UI Symbol");

        private Mode _mode = Mode.Encrypt;
        private bool _busy;
        private bool _suppressInputNotice;
        private bool _customMaximized;
        private int _inputRevision;
        private Rect _restoreBounds;

        private TextBox _inputBox;
        private TextBox _outputBox;
        private TextBlock _inputPlaceholder;
        private TextBlock _outputPlaceholder;
        private TextBlock _inputKind;
        private TextBlock _outputKind;
        private TextBlock _inputCount;
        private TextBlock _outputCount;
        private TextBlock _statusText;
        private PasswordBox _passphraseBox;
        private Button _actionButton;
        private Button _copyButton;
        private Button _clearButton;
        private Button _swapButton;
        private Button _maximizeButton;
        private Button _encryptChoice;
        private Button _decryptChoice;

        public MainWindow()
        {
            ConfigureWindow();
            BuildInterface();
            ApplyModeVisuals();
            SetStatus("[READY] 在左侧输入明文，然后按「加密」。", false);
            Loaded += OnLoaded;
        }

        private static SolidColorBrush FrozenBrush(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        private void ConfigureWindow()
        {
            Title = "NOIR — 私密文本";
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.CanResize;
            Background = Ink;
            Foreground = Paper;
            FontFamily = UiFont;
            SnapsToDevicePixels = true;
            UseLayoutRounding = true;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            Width = 1000;
            Height = 600;
            MinWidth = 900;
            MinHeight = 560;

            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.Grayscale);

            var chrome = new WindowChrome
            {
                CaptionHeight = 0,
                ResizeBorderThickness = new Thickness(6),
                GlassFrameThickness = new Thickness(0),
                CornerRadius = new CornerRadius(0),
                UseAeroCaptionButtons = false
            };
            WindowChrome.SetWindowChrome(this, chrome);

            PreviewKeyDown += OnPreviewKeyDown;
            StateChanged += OnWindowStateChanged;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            var dpi = VisualTreeHelper.GetDpi(this);
            double scaleX = dpi.DpiScaleX <= 0 ? 1.0 : dpi.DpiScaleX;
            double scaleY = dpi.DpiScaleY <= 0 ? 1.0 : dpi.DpiScaleY;

            Rect workArea = SystemParameters.WorkArea;
            double workWidth = workArea.Width / scaleX;
            double workHeight = workArea.Height / scaleY;
            double availableWidth = Math.Max(640, workWidth - 48);
            double availableHeight = Math.Max(480, workHeight - 24);

            MinWidth = Math.Min(900, availableWidth);
            MinHeight = Math.Min(560, availableHeight);

            double targetWidth = Math.Min(1180, availableWidth);
            double targetHeight = Math.Min(760, availableHeight);

            Width = targetWidth;
            Height = targetHeight;
            Left = workArea.Left / scaleX + (workWidth - targetWidth) / 2;
            Top = workArea.Top / scaleY + (workHeight - targetHeight) / 2;
        }

        private void BuildInterface()
        {
            var outer = new Border
            {
                Background = Ink,
                BorderBrush = Paper,
                BorderThickness = new Thickness(1)
            };
            Content = outer;

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(56) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            outer.Child = root;

            UIElement titleBar = BuildTitleBar();
            Grid.SetRow(titleBar, 0);
            root.Children.Add(titleBar);

            var rule = new Rectangle { Fill = Paper, Height = 1 };
            Grid.SetRow(rule, 1);
            root.Children.Add(rule);

            UIElement content = BuildContent();
            Grid.SetRow(content, 2);
            root.Children.Add(content);
        }

        private UIElement BuildTitleBar()
        {
            var bar = new Grid { Background = Ink };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var brand = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(18, 0, 0, 0)
            };

            var mark = new Border
            {
                Width = 26,
                Height = 26,
                Background = Paper,
                SnapsToDevicePixels = true
            };
            mark.Child = new TextBlock
            {
                Text = "N",
                Foreground = Ink,
                FontFamily = BrandFont,
                FontWeight = FontWeights.Bold,
                FontSize = 15,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            brand.Children.Add(mark);

            brand.Children.Add(new TextBlock
            {
                Text = "NOIR",
                Foreground = Paper,
                FontFamily = BrandFont,
                FontWeight = FontWeights.Bold,
                FontSize = 19,
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            });

            brand.Children.Add(new Rectangle
            {
                Width = 1,
                Height = 14,
                Fill = Paper,
                Margin = new Thickness(14, 0, 14, 0),
                VerticalAlignment = VerticalAlignment.Center
            });

            brand.Children.Add(new TextBlock
            {
                Text = "PRIVATE TEXT SYSTEM",
                Foreground = Paper,
                FontFamily = MonoFont,
                FontSize = 9.0,
                VerticalAlignment = VerticalAlignment.Center
            });

            bar.Children.Add(brand);
            Grid.SetColumn(brand, 0);

            var windowControls = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };

            Button minimize = MakeWindowButton("—", 13, "最小化", delegate { WindowState = WindowState.Minimized; });
            Button maximize = MakeWindowButton("□", 11, "最大化", ToggleMaximize);
            Button close = MakeWindowButton("✕", 11, "关闭", Close);
            _maximizeButton = maximize;

            windowControls.Children.Add(minimize);
            windowControls.Children.Add(maximize);
            windowControls.Children.Add(close);
            bar.Children.Add(windowControls);
            Grid.SetColumn(windowControls, 1);

            bar.MouseLeftButtonDown += OnTitleBarMouseDown;
            return bar;
        }

        private UIElement BuildContent()
        {
            var content = new Grid
            {
                Margin = new Thickness(34, 22, 34, 18)
            };
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            UIElement header = BuildHeader();
            Grid.SetRow(header, 0);
            content.Children.Add(header);

            UIElement editors = BuildEditors();
            Grid.SetRow(editors, 1);
            content.Children.Add(editors);

            UIElement status = BuildStatusLine();
            Grid.SetRow(status, 2);
            content.Children.Add(status);

            UIElement actions = BuildActionRow();
            Grid.SetRow(actions, 3);
            content.Children.Add(actions);

            return content;
        }

        private UIElement BuildHeader()
        {
            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var left = new StackPanel
            {
                Orientation = Orientation.Vertical,
                VerticalAlignment = VerticalAlignment.Bottom
            };

            var eyebrow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 10)
            };
            eyebrow.Children.Add(new Rectangle
            {
                Width = 18,
                Height = 2,
                Fill = Paper,
                Margin = new Thickness(0, 0, 9, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            eyebrow.Children.Add(new TextBlock
            {
                Text = "PRIVATE TEXT SYSTEM  /  V1.0",
                Foreground = Paper,
                FontFamily = MonoFont,
                FontSize = 9.0,
                VerticalAlignment = VerticalAlignment.Center
            });
            left.Children.Add(eyebrow);

            left.Children.Add(new TextBlock
            {
                Text = "把文字，\n藏进黑夜。",
                Foreground = Paper,
                FontFamily = UiFont,
                FontWeight = FontWeights.SemiBold,
                FontSize = 38,
                LineHeight = 45,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight
            });

            left.Children.Add(new TextBlock
            {
                Text = "自研 NOIR-ARX 加密 · 本地运行 · 不联网",
                Foreground = Paper,
                FontFamily = UiFont,
                FontSize = 10.5,
                Margin = new Thickness(0, 7, 0, 0)
            });

            header.Children.Add(left);
            Grid.SetColumn(left, 0);

            var modeBlock = new StackPanel
            {
                Orientation = Orientation.Vertical,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(30, 0, 0, 2)
            };

            modeBlock.Children.Add(new TextBlock
            {
                Text = "MODE / 模式",
                Foreground = Paper,
                FontFamily = MonoFont,
                FontSize = 9.0,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 0, 0, 7)
            });

            var modeBorder = new Border
            {
                BorderBrush = Paper,
                BorderThickness = new Thickness(1),
                Background = Ink,
                Height = 42,
                Width = 340
            };

            var modeGrid = new Grid();
            modeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            modeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
            modeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            modeBorder.Child = modeGrid;

            _encryptChoice = MakeModeChoice("加密  ENCRYPT", "加密模式");
            _decryptChoice = MakeModeChoice("解密  DECRYPT", "解密模式");
            Grid.SetColumn(_encryptChoice, 0);
            Grid.SetColumn(_decryptChoice, 2);
            modeGrid.Children.Add(_encryptChoice);
            var modeSeparator = new Rectangle { Fill = Paper, Width = 1 };
            Grid.SetColumn(modeSeparator, 1);
            modeGrid.Children.Add(modeSeparator);
            modeGrid.Children.Add(_decryptChoice);

            _encryptChoice.Click += delegate { RequestMode(Mode.Encrypt); };
            _decryptChoice.Click += delegate { RequestMode(Mode.Decrypt); };
            _encryptChoice.MouseEnter += delegate { HoverModeButton(_encryptChoice); };
            _encryptChoice.MouseLeave += delegate { ApplyModeButtonStyle(_encryptChoice, _mode == Mode.Encrypt); };
            _decryptChoice.MouseEnter += delegate { HoverModeButton(_decryptChoice); };
            _decryptChoice.MouseLeave += delegate { ApplyModeButtonStyle(_decryptChoice, _mode == Mode.Decrypt); };

            modeBlock.Children.Add(modeBorder);
            header.Children.Add(modeBlock);
            Grid.SetColumn(modeBlock, 1);

            return header;
        }

        private UIElement BuildEditors()
        {
            var editors = new Grid
            {
                Margin = new Thickness(0, 18, 0, 0)
            };
            editors.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            editors.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
            editors.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            EditorCard inputCard = CreateEditorCard("01", "INPUT", false);
            EditorCard outputCard = CreateEditorCard("02", "OUTPUT", true);
            _inputBox = inputCard.Box;
            _outputBox = outputCard.Box;
            _inputPlaceholder = inputCard.Placeholder;
            _outputPlaceholder = outputCard.Placeholder;
            _inputKind = inputCard.Kind;
            _outputKind = outputCard.Kind;
            _inputCount = inputCard.Count;
            _outputCount = outputCard.Count;

            _inputBox.TextChanged += OnInputChanged;
            _inputBox.GotKeyboardFocus += OnEditorFocusChanged;
            _inputBox.LostKeyboardFocus += OnEditorFocusChanged;
            _outputBox.TextChanged += OnOutputChanged;
            _outputBox.GotKeyboardFocus += OnEditorFocusChanged;
            _outputBox.LostKeyboardFocus += OnEditorFocusChanged;

            editors.Children.Add(inputCard.Root);
            Grid.SetColumn(inputCard.Root, 0);
            editors.Children.Add(outputCard.Root);
            Grid.SetColumn(outputCard.Root, 2);

            var bridge = new Grid();
            bridge.Children.Add(new Rectangle
            {
                Fill = Paper,
                Width = 1,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Stretch,
                Margin = new Thickness(0, 2, 0, 2)
            });

            _swapButton = MakeButton("⇄", false, 30, 30, SwapOutputToInput);
            _swapButton.FontFamily = SymbolFont;
            _swapButton.FontSize = 14;
            _swapButton.Padding = new Thickness(0);
            _swapButton.HorizontalAlignment = HorizontalAlignment.Center;
            _swapButton.VerticalAlignment = VerticalAlignment.Center;
            _swapButton.ToolTip = MakeToolTip("把结果移到输入，并切换模式  (Ctrl+Shift+S)");
            AutomationProperties.SetName(_swapButton, "将结果用作输入");
            bridge.Children.Add(_swapButton);

            editors.Children.Add(bridge);
            Grid.SetColumn(bridge, 1);

            return editors;
        }

        private UIElement BuildStatusLine()
        {
            var line = new Grid
            {
                Margin = new Thickness(0, 11, 0, 0),
                Height = 17
            };
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _statusText = new TextBlock
            {
                Foreground = Paper,
                FontFamily = UiFont,
                FontSize = 9.5,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            line.Children.Add(_statusText);
            Grid.SetColumn(_statusText, 0);

            var meta = new TextBlock
            {
                Text = "CTRL + ENTER 执行   ·   LOCAL ONLY",
                Foreground = Paper,
                FontFamily = MonoFont,
                FontSize = 9.0,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(16, 0, 0, 0)
            };
            line.Children.Add(meta);
            Grid.SetColumn(meta, 1);

            return line;
        }

        private UIElement BuildActionRow()
        {
            var row = new Grid
            {
                Margin = new Thickness(0, 12, 0, 0)
            };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var passBlock = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Width = 300,
                VerticalAlignment = VerticalAlignment.Bottom
            };

            var passLabels = new Grid { Width = 300, Margin = new Thickness(0, 0, 0, 5) };
            passLabels.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            passLabels.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var passTitle = new TextBlock
            {
                Foreground = Paper,
                FontFamily = MonoFont,
                FontSize = 9.0,
                VerticalAlignment = VerticalAlignment.Center
            };
            passTitle.Inlines.Add(new Run("PASSPHRASE  /  口令"));
            passTitle.Inlines.Add(new Run("（可选）") { FontFamily = UiFont, FontSize = 9.0 });
            passLabels.Children.Add(passTitle);
            Grid.SetColumn(passTitle, 0);

            var passHint = new TextBlock
            {
                Text = "留空 = 内置密钥",
                Foreground = Paper,
                FontFamily = UiFont,
                FontSize = 9.0,
                VerticalAlignment = VerticalAlignment.Center
            };
            passLabels.Children.Add(passHint);
            Grid.SetColumn(passHint, 1);
            passBlock.Children.Add(passLabels);

            _passphraseBox = new PasswordBox
            {
                Width = 300,
                Height = 38,
                Background = Ink,
                Foreground = Paper,
                BorderBrush = Paper,
                BorderThickness = new Thickness(1),
                CaretBrush = Paper,
                Padding = new Thickness(10, 7, 10, 6),
                FontFamily = MonoFont,
                FontSize = 12,
                PasswordChar = '●',
                SelectionBrush = Paper,
                SelectionTextBrush = Ink,
                SelectionOpacity = 1,
                FocusVisualStyle = null,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            _passphraseBox.GotKeyboardFocus += delegate { _passphraseBox.BorderThickness = new Thickness(2); };
            _passphraseBox.LostKeyboardFocus += delegate { _passphraseBox.BorderThickness = new Thickness(1); };
            ContextMenuService.SetIsEnabled(_passphraseBox, false);
            AutomationProperties.SetName(_passphraseBox, "口令，可选；留空使用内置密钥");
            passBlock.Children.Add(_passphraseBox);

            row.Children.Add(passBlock);
            Grid.SetColumn(passBlock, 0);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom
            };

            _clearButton = MakeButton("清空", false, 82, 40, ClearAll);
            _copyButton = MakeButton("复制结果", false, 112, 40, CopyOutput);
            _actionButton = MakeButton("加密文本  →", true, 174, 40, delegate { RunAction(); });

            _clearButton.ToolTip = MakeToolTip("清空输入、输出和口令  (Ctrl+Shift+X)");
            _copyButton.ToolTip = MakeToolTip("复制输出  (Ctrl+Shift+C)");
            _actionButton.ToolTip = MakeToolTip("执行操作  (Ctrl+Enter)");

            buttons.Children.Add(_clearButton);
            buttons.Children.Add(_copyButton);
            buttons.Children.Add(_actionButton);
            _clearButton.Margin = new Thickness(0);
            _copyButton.Margin = new Thickness(10, 0, 0, 0);
            _actionButton.Margin = new Thickness(10, 0, 0, 0);

            row.Children.Add(buttons);
            Grid.SetColumn(buttons, 2);

            return row;
        }

        private EditorCard CreateEditorCard(string number, string label, bool readOnly)
        {
            var card = new Border
            {
                Background = Ink,
                BorderBrush = Paper,
                BorderThickness = new Thickness(1)
            };

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(43) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            card.Child = grid;

            var header = new Border
            {
                BorderBrush = Paper,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Background = Ink
            };
            grid.Children.Add(header);

            var headerGrid = new Grid { Margin = new Thickness(13, 0, 13, 0) };
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.Child = headerGrid;

            var badge = new Border
            {
                Width = 22,
                Height = 22,
                Background = Paper,
                VerticalAlignment = VerticalAlignment.Center
            };
            badge.Child = new TextBlock
            {
                Text = number,
                Foreground = Ink,
                FontFamily = MonoFont,
                FontWeight = FontWeights.Bold,
                FontSize = 9.0,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            headerGrid.Children.Add(badge);
            Grid.SetColumn(badge, 0);

            var titleStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0)
            };
            titleStack.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = Paper,
                FontFamily = MonoFont,
                FontWeight = FontWeights.Bold,
                FontSize = 9.0,
                VerticalAlignment = VerticalAlignment.Center
            });
            titleStack.Children.Add(new TextBlock
            {
                Text = "  /  ",
                Foreground = Paper,
                FontFamily = MonoFont,
                FontSize = 9.0,
                VerticalAlignment = VerticalAlignment.Center
            });

            var kindText = new TextBlock
            {
                Foreground = Paper,
                FontFamily = UiFont,
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center
            };
            titleStack.Children.Add(kindText);
            headerGrid.Children.Add(titleStack);
            Grid.SetColumn(titleStack, 1);

            var countText = new TextBlock
            {
                Text = "0 字符",
                Foreground = Paper,
                FontFamily = MonoFont,
                FontSize = 9.0,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            headerGrid.Children.Add(countText);
            Grid.SetColumn(countText, 2);

            var body = new Grid
            {
                Margin = new Thickness(15, 13, 15, 12)
            };
            Grid.SetRow(body, 1);
            grid.Children.Add(body);

            var box = new TextBox
            {
                Background = Ink,
                Foreground = Paper,
                CaretBrush = Paper,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                FontFamily = MonoFont,
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = true,
                AcceptsTab = false,
                VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                SelectionBrush = Paper,
                SelectionOpacity = 1,
                SelectionTextBrush = Ink,
                IsReadOnly = readOnly,
                IsReadOnlyCaretVisible = readOnly,
                FocusVisualStyle = null,
                VerticalContentAlignment = VerticalAlignment.Top,
                UndoLimit = 200
            };
            SpellCheck.SetIsEnabled(box, false);
            TextOptions.SetTextFormattingMode(box, TextFormattingMode.Display);
            AutomationProperties.SetName(box, readOnly ? "输出文本" : "输入文本");
            box.ContextMenu = CreateTextContextMenu(box);
            body.Children.Add(box);

            var placeholder = new TextBlock
            {
                Foreground = Paper,
                FontFamily = MonoFont,
                FontSize = 10.5,
                TextWrapping = TextWrapping.Wrap,
                IsHitTestVisible = false,
                VerticalAlignment = VerticalAlignment.Top,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 1, 0, 0)
            };
            body.Children.Add(placeholder);

            return new EditorCard
            {
                Root = card,
                Box = box,
                Placeholder = placeholder,
                Kind = kindText,
                Count = countText
            };
        }

        private Button MakeModeChoice(string text, string accessibleName)
        {
            var choice = new Button
            {
                Content = text,
                Background = Ink,
                Foreground = Paper,
                BorderThickness = new Thickness(0),
                FontFamily = UiFont,
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Cursor = Cursors.Hand,
                FocusVisualStyle = null,
                Template = BuildModeTileTemplate(),
                Padding = new Thickness(4, 0, 4, 0)
            };
            AutomationProperties.SetName(choice, accessibleName);
            choice.ToolTip = MakeToolTip(accessibleName);
            return choice;
        }

        private ControlTemplate BuildModeTileTemplate()
        {
            var template = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            border.SetValue(Border.SnapsToDevicePixelsProperty, true);

            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            presenter.SetValue(TextElement.ForegroundProperty,
                new TemplateBindingExtension(Control.ForegroundProperty));
            border.AppendChild(presenter);
            template.VisualTree = border;
            return template;
        }

        private Button MakeButton(string text, bool primary, double width, double height, Action action)
        {
            var button = new Button
            {
                Content = text,
                Width = width,
                Height = height,
                Background = primary ? Paper : Ink,
                Foreground = primary ? Ink : Paper,
                BorderBrush = Paper,
                BorderThickness = new Thickness(1),
                FontFamily = UiFont,
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Hand,
                FocusVisualStyle = null,
                Template = BuildButtonTemplate(primary),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(8, 0, 8, 0)
            };
            button.Click += delegate { action(); };
            AddButtonStates(
                button,
                primary ? Paper : Ink,
                primary ? Ink : Paper,
                primary ? Ink : Paper,
                primary ? Paper : Ink,
                new Thickness(1),
                new Thickness(2));
            return button;
        }

        private static void AddButtonStates(
            Button button,
            Brush normalBackground,
            Brush normalForeground,
            Brush hoverBackground,
            Brush hoverForeground,
            Thickness normalBorder,
            Thickness focusBorder)
        {
            button.MouseEnter += delegate
            {
                if (!button.IsEnabled) return;
                button.Background = hoverBackground;
                button.Foreground = hoverForeground;
            };
            button.MouseLeave += delegate
            {
                button.Background = normalBackground;
                button.Foreground = normalForeground;
            };
            button.GotKeyboardFocus += delegate
            {
                button.BorderThickness = focusBorder;
            };
            button.LostKeyboardFocus += delegate
            {
                button.BorderThickness = normalBorder;
            };
        }

        private Button MakeWindowButton(string glyph, double fontSize, string accessibleName, Action action)
        {
            var content = new TextBlock
            {
                Text = glyph,
                FontFamily = SymbolFont,
                FontSize = fontSize,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            var button = new Button
            {
                Content = content,
                Width = 42,
                Height = 32,
                Margin = new Thickness(2, 0, 0, 0),
                Background = Brushes.Transparent,
                Foreground = Paper,
                BorderBrush = Paper,
                BorderThickness = new Thickness(0),
                FontFamily = SymbolFont,
                FontSize = fontSize,
                Cursor = Cursors.Hand,
                FocusVisualStyle = null,
                Focusable = false,
                Template = BuildButtonTemplate(false),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(0)
            };
            button.Click += delegate { action(); };
            AutomationProperties.SetName(button, accessibleName);
            button.ToolTip = MakeToolTip(accessibleName);
            AddButtonStates(
                button,
                Brushes.Transparent,
                Paper,
                Paper,
                Ink,
                new Thickness(0),
                new Thickness(0));
            return button;
        }

        private ControlTemplate BuildButtonTemplate(bool primary)
        {
            var template = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
            border.SetValue(Border.SnapsToDevicePixelsProperty, true);

            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            presenter.SetValue(TextElement.ForegroundProperty,
                new TemplateBindingExtension(Control.ForegroundProperty));
            border.AppendChild(presenter);
            template.VisualTree = border;

            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Control.BackgroundProperty, primary ? Ink : Paper));
            hover.Setters.Add(new Setter(Control.ForegroundProperty, primary ? Paper : Ink));
            hover.Setters.Add(new Setter(Control.BorderBrushProperty, Paper));
            template.Triggers.Add(hover);

            var pressed = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
            pressed.Setters.Add(new Setter(Control.BackgroundProperty, primary ? Ink : Paper));
            pressed.Setters.Add(new Setter(Control.ForegroundProperty, primary ? Paper : Ink));
            template.Triggers.Add(pressed);

            var focused = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
            focused.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(2)));
            template.Triggers.Add(focused);

            return template;
        }

        private ToolTip MakeToolTip(string text)
        {
            return new ToolTip
            {
                Content = new TextBlock
                {
                    Text = text,
                    Foreground = Paper,
                    FontFamily = UiFont,
                    FontSize = 10
                },
                Background = Ink,
                Foreground = Paper,
                BorderBrush = Paper,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 4, 8, 4),
                FontFamily = UiFont,
                FontSize = 10,
                HasDropShadow = false
            };
        }

        private ContextMenu CreateTextContextMenu(TextBox box)
        {
            var menu = new ContextMenu
            {
                Background = Ink,
                Foreground = Paper,
                BorderBrush = Paper,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(2),
                FontFamily = UiFont,
                FontSize = 10.5,
                HasDropShadow = false
            };

            menu.Items.Add(MakeMenuItem("撤销", ApplicationCommands.Undo, box));
            menu.Items.Add(MakeMenuItem("剪切", ApplicationCommands.Cut, box));
            menu.Items.Add(MakeMenuItem("复制", ApplicationCommands.Copy, box));
            menu.Items.Add(MakeMenuItem("粘贴", ApplicationCommands.Paste, box));
            menu.Items.Add(MakeMenuItem("全选", ApplicationCommands.SelectAll, box));
            return menu;
        }

        private MenuItem MakeMenuItem(string header, RoutedCommand command, IInputElement target)
        {
            var item = new MenuItem
            {
                Header = header,
                Command = command,
                CommandTarget = target,
                Background = Ink,
                Foreground = Paper,
                BorderBrush = Ink,
                Padding = new Thickness(11, 5, 22, 5),
                FontFamily = UiFont,
                FontSize = 10.5,
                Cursor = Cursors.Hand,
                Template = BuildMenuItemTemplate()
            };
            return item;
        }

        private ControlTemplate BuildMenuItemTemplate()
        {
            var template = new ControlTemplate(typeof(MenuItem));
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new Thickness(0));
            border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));

            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.ContentSourceProperty, "Header");
            presenter.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);
            presenter.SetValue(TextElement.ForegroundProperty,
                new TemplateBindingExtension(Control.ForegroundProperty));
            border.AppendChild(presenter);
            template.VisualTree = border;

            var highlighted = new Trigger { Property = MenuItem.IsHighlightedProperty, Value = true };
            highlighted.Setters.Add(new Setter(Control.BackgroundProperty, Paper));
            highlighted.Setters.Add(new Setter(Control.ForegroundProperty, Ink));
            template.Triggers.Add(highlighted);

            return template;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            UpdateEditorPlaceholders();
            _inputBox.Focus();
            Keyboard.Focus(_inputBox);
            _inputBox.CaretIndex = 0;
        }

        private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;

            DependencyObject source = e.OriginalSource as DependencyObject;
            while (source != null)
            {
                if (source is ButtonBase) return;
                if (source == this) break;
                source = VisualTreeHelper.GetParent(source);
            }

            if (e.ClickCount == 2)
            {
                ToggleMaximize();
                e.Handled = true;
                return;
            }

            if (_customMaximized) RestoreCustomMaximize();
            try
            {
                DragMove();
            }
            catch (InvalidOperationException)
            {
            }
        }

        private void ToggleMaximize()
        {
            if (_customMaximized)
            {
                RestoreCustomMaximize();
            }
            else
            {
                MaximizeCustom();
            }
        }

        private void MaximizeCustom()
        {
            if (_customMaximized) return;

            _restoreBounds = new Rect(Left, Top, Width, Height);
            if (WindowState != WindowState.Normal)
                WindowState = WindowState.Normal;

            var dpi = VisualTreeHelper.GetDpi(this);
            Rect work = GetCurrentMonitorWorkAreaDip(dpi);
            ResizeMode = ResizeMode.NoResize;
            Left = work.Left;
            Top = work.Top;
            Width = work.Width;
            Height = work.Height;
            _customMaximized = true;
            UpdateMaximizeButton();
        }

        private void RestoreCustomMaximize()
        {
            if (!_customMaximized) return;

            ResizeMode = ResizeMode.CanResize;
            Left = _restoreBounds.Left;
            Top = _restoreBounds.Top;
            Width = _restoreBounds.Width;
            Height = _restoreBounds.Height;
            _customMaximized = false;
            UpdateMaximizeButton();
        }

        private Rect GetCurrentMonitorWorkAreaDip(DpiScale dpi)
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            if (handle != IntPtr.Zero)
            {
                IntPtr monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
                if (monitor != IntPtr.Zero)
                {
                    var info = new MonitorInfo();
                    info.Size = Marshal.SizeOf(typeof(MonitorInfo));
                    if (GetMonitorInfo(monitor, ref info))
                    {
                        double scaleX = dpi.DpiScaleX <= 0 ? 1.0 : dpi.DpiScaleX;
                        double scaleY = dpi.DpiScaleY <= 0 ? 1.0 : dpi.DpiScaleY;
                        return new Rect(
                            info.Work.Left / scaleX,
                            info.Work.Top / scaleY,
                            (info.Work.Right - info.Work.Left) / scaleX,
                            (info.Work.Bottom - info.Work.Top) / scaleY);
                    }
                }
            }

            Rect fallback = SystemParameters.WorkArea;
            return new Rect(
                fallback.Left / dpi.DpiScaleX,
                fallback.Top / dpi.DpiScaleY,
                fallback.Width / dpi.DpiScaleX,
                fallback.Height / dpi.DpiScaleY);
        }

        private void OnWindowStateChanged(object sender, EventArgs e)
        {
            if (!_customMaximized && WindowState == WindowState.Maximized)
            {
                WindowState = WindowState.Normal;
                MaximizeCustom();
                return;
            }

            UpdateMaximizeButton();
        }

        private void UpdateMaximizeButton()
        {
            if (_maximizeButton == null) return;
            _maximizeButton.Content = new TextBlock
            {
                Text = _customMaximized ? "❐" : "□",
                FontFamily = SymbolFont,
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            string accessibleName = _customMaximized ? "还原" : "最大化";
            AutomationProperties.SetName(_maximizeButton, accessibleName);
            _maximizeButton.ToolTip = MakeToolTip(accessibleName);
        }

        private void RequestMode(Mode requested)
        {
            if (_busy)
            {
                SetStatus("[WORK] 请等待当前操作完成。", true);
                return;
            }

            if (requested == _mode) return;

            _mode = requested;
            ApplyModeVisuals();
            if (_outputBox != null && _outputBox.Text.Length > 0)
                SetOutput(string.Empty);
            SetDefaultStatus();
        }

        private void HoverModeButton(Button button)
        {
            bool selected = button == _encryptChoice
                ? _mode == Mode.Encrypt
                : _mode == Mode.Decrypt;
            if (!selected)
            {
                button.Background = Paper;
                button.Foreground = Ink;
            }
        }

        private void ApplyModeButtonStyle(Button button, bool selected)
        {
            button.Background = selected ? Paper : Ink;
            button.Foreground = selected ? Ink : Paper;
            AutomationProperties.SetItemStatus(button, selected ? "已选择" : "未选择");
        }

        private void ApplyModeVisuals()
        {
            ApplyModeButtonStyle(_encryptChoice, _mode == Mode.Encrypt);
            ApplyModeButtonStyle(_decryptChoice, _mode == Mode.Decrypt);

            if (_mode == Mode.Encrypt)
            {
                _inputKind.Text = "明文  /  PLAINTEXT";
                _outputKind.Text = "密文  /  CIPHERTEXT";
                _inputPlaceholder.Text = "// 输入或粘贴任意明文";
                _outputPlaceholder.Text = "// 加密结果将显示在这里";
                _actionButton.Content = "加密文本  →";
                AutomationProperties.SetName(_actionButton, "加密文本");
            }
            else
            {
                _inputKind.Text = "密文  /  CIPHERTEXT";
                _outputKind.Text = "明文  /  PLAINTEXT";
                _inputPlaceholder.Text = "// 粘贴以 noir1. 开头的 NOIR 密文";
                _outputPlaceholder.Text = "// 解密后的明文将显示在这里";
                _actionButton.Content = "解密文本  →";
                AutomationProperties.SetName(_actionButton, "解密文本");
            }

            UpdateEditorPlaceholders();
        }

        private void SetDefaultStatus()
        {
            if (_mode == Mode.Encrypt)
                SetStatus("[READY] 在左侧输入明文，然后按「加密」。", false);
            else
                SetStatus("[READY] 粘贴 noir1. 密文，然后按「解密」。", false);
        }

        private void OnInputChanged(object sender, TextChangedEventArgs e)
        {
            _inputRevision++;
            _inputCount.Text = FormatCount(_inputBox.Text);
            UpdateEditorPlaceholders();

            if (!_suppressInputNotice && !_busy && _outputBox != null && _outputBox.Text.Length > 0)
                SetStatus("[EDIT] 输入已更改，请重新执行。", false);
        }

        private void OnOutputChanged(object sender, TextChangedEventArgs e)
        {
            _outputCount.Text = FormatCount(_outputBox.Text);
            UpdateEditorPlaceholders();
        }

        private void OnEditorFocusChanged(object sender, KeyboardFocusChangedEventArgs e)
        {
            UpdateEditorPlaceholders();
        }

        private void UpdateEditorPlaceholders()
        {
            if (_inputBox == null || _outputBox == null) return;

            _inputPlaceholder.Visibility =
                _inputBox.Text.Length == 0 && !_inputBox.IsKeyboardFocusWithin
                ? Visibility.Visible
                : Visibility.Collapsed;

            _outputPlaceholder.Visibility =
                _outputBox.Text.Length == 0 && !_outputBox.IsKeyboardFocusWithin
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private static string FormatCount(string text)
        {
            return (text ?? string.Empty).Length.ToString("N0", CultureInfo.CurrentCulture) + " 字符";
        }

        private void SetOutput(string text)
        {
            _outputBox.Text = text ?? string.Empty;
            _outputBox.CaretIndex = 0;
            _outputBox.ScrollToHome();
            _outputCount.Text = FormatCount(_outputBox.Text);
            UpdateEditorPlaceholders();
        }

        private async void RunAction()
        {
            if (_busy) return;

            string input = _inputBox.Text;
            string passphrase = _passphraseBox.Password;
            int revision = _inputRevision;

            if (input.Length == 0)
            {
                SetStatus("[!] 请先输入要处理的文本。", true);
                _inputBox.Focus();
                return;
            }

            _busy = true;
            _actionButton.Content = "处理中…";
            SetOutput(string.Empty);
            SetStatus("[WORK] 正在派生密钥并处理文本，请稍候…", false);

            try
            {
                string result;
                if (_mode == Mode.Encrypt)
                {
                    result = await Task.Run(() => NoirCipher.Encrypt(input, passphrase));
                    SetOutput(result);
                    SetStatus("[OK] 已加密 · " + FormatCount(input) + " → " + FormatCount(result), false);
                }
                else
                {
                    result = await Task.Run(() => NoirCipher.Decrypt(input, passphrase));
                    SetOutput(result);
                    SetStatus("[OK] 已解密 · " + FormatCount(result), false);
                }

                if (revision != _inputRevision)
                    SetStatus("[EDIT] 处理期间输入已更改，结果对应更早的文本。", true);
                else if (passphrase != _passphraseBox.Password)
                    SetStatus("[EDIT] 处理期间口令已更改，结果使用先前的口令。", true);
            }
            catch (NoirCipherException ex)
            {
                SetStatus("[!] " + ex.Message, true);
            }
            catch (Exception ex)
            {
                SetStatus("[!] 操作失败：" + ex.Message, true);
            }
            finally
            {
                _busy = false;
                if (_actionButton != null)
                {
                    _actionButton.Content = _mode == Mode.Encrypt ? "加密文本  →" : "解密文本  →";
                }

                if (_mode == Mode.Decrypt && _outputBox.Text.Length > 0)
                    _outputBox.Focus();
                else
                    _inputBox.Focus();
            }
        }

        private void CopyOutput()
        {
            if (_busy) return;
            string text = _outputBox.Text;
            if (text.Length == 0)
            {
                SetStatus("[!] 还没有可复制的结果。", true);
                return;
            }

            if (TrySetClipboard(text))
                SetStatus("[OK] 已把输出复制到剪贴板。", false);
            else
                SetStatus("[!] 剪贴板暂时被占用，请重试。", true);
        }

        private static bool TrySetClipboard(string text)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    Clipboard.SetText(text);
                    return true;
                }
                catch (COMException)
                {
                    Thread.Sleep(35);
                }
                catch (ExternalException)
                {
                    Thread.Sleep(35);
                }
            }
            return false;
        }

        private void ClearAll()
        {
            if (_busy) return;

            _suppressInputNotice = true;
            try
            {
                _inputBox.Clear();
                _outputBox.Clear();
                _passphraseBox.Clear();
            }
            finally
            {
                _suppressInputNotice = false;
            }

            _inputCount.Text = "0 字符";
            _outputCount.Text = "0 字符";
            UpdateEditorPlaceholders();
            SetStatus("[READY] 已清空输入、输出和口令。", false);
            _inputBox.Focus();
        }

        private void SwapOutputToInput()
        {
            if (_busy) return;
            if (_outputBox.Text.Length == 0)
            {
                SetStatus("[!] 还没有可以复用的结果。", true);
                return;
            }

            string result = _outputBox.Text;
            _mode = _mode == Mode.Encrypt ? Mode.Decrypt : Mode.Encrypt;
            ApplyModeVisuals();

            _suppressInputNotice = true;
            try
            {
                _inputBox.Text = result;
                _outputBox.Clear();
            }
            finally
            {
                _suppressInputNotice = false;
            }

            _inputCount.Text = FormatCount(_inputBox.Text);
            _outputCount.Text = "0 字符";
            UpdateEditorPlaceholders();
            SetStatus(_mode == Mode.Decrypt
                ? "[OK] 结果已移到输入，并切换到解密模式。"
                : "[OK] 结果已移到输入，并切换到加密模式。", false);
            _inputBox.Focus();
        }

        private void SetStatus(string message, bool emphasize)
        {
            _statusText.Text = message ?? string.Empty;
            _statusText.FontWeight = emphasize ? FontWeights.Bold : FontWeights.Normal;
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            bool control = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;

            if (control && e.Key == Key.Enter)
            {
                RunAction();
                e.Handled = true;
            }
            else if (control && shift && e.Key == Key.C)
            {
                CopyOutput();
                e.Handled = true;
            }
            else if (control && shift && e.Key == Key.X)
            {
                ClearAll();
                e.Handled = true;
            }
            else if (control && shift && e.Key == Key.S)
            {
                SwapOutputToInput();
                e.Handled = true;
            }
        }

        private const uint MonitorDefaultToNearest = 2;

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MonitorInfo
        {
            public int Size;
            public NativeRect Monitor;
            public NativeRect Work;
            public uint Flags;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

        private sealed class EditorCard
        {
            public Border Root;
            public TextBox Box;
            public TextBlock Placeholder;
            public TextBlock Kind;
            public TextBlock Count;
        }
    }
}
