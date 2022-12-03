﻿using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Configuration;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml;

using ImageMagick;
using CompactExifLib;
using System.Windows.Interop;
using System.Runtime.InteropServices;

namespace TouchMeta
{
    public class MetaInfo
    {
        public bool ShowXMP { get; set; } = false;
        public bool TouchProfiles { get; set; } = true;
        public ChangePropertyType ChangeProperties { get; set; } = ChangePropertyType.All;

        public DateTime? DateCreated { get; set; } = null;
        public DateTime? DateModified { get; set; } = null;
        public DateTime? DateAccesed { get; set; } = null;

        public DateTime? DateAcquired { get; set; } = null;
        public DateTime? DateTaken { get; set; } = null;

        public string Title { get; set; } = null;
        public string Subject { get; set; } = null;
        public string Keywords { get; set; } = null;
        public string Comment { get; set; } = null;
        public string Authors { get; set; } = null;
        public string Copyrights { get; set; } = null;

        public int? RatingPercent { get; set; } = null;
        public int? Rating { get; set; } = null;

        public Dictionary<string, string> Attributes { get; set; } = new Dictionary<string, string>();
        public Dictionary<string, IImageProfile> Profiles { get; set; } = new Dictionary<string, IImageProfile>();
    }

    public enum ChangePropertyMode { None = 0, Append = 1, Remove = 2, Replace = 3, Empty = 4 };

    [Flags]
    public enum ChangePropertyType
    {
        None = 0x0000, //0b00000000,
        Title = 0x0001, //0b00000001,
        Subject = 0x0002, //0b00000010,
        Keywords = 0x0004, //0b00000100,
        Comment = 0x0008, //0b00001000,
        Authors = 0x0010, //0b00010000,
        Copyrights = 0x0020, //0b00100000,
        Rating = 0x0040, //0b01000000,
        Ranking = 0x0080, //0b10000000,

        DateTime = 0x4000,
        Smart = 0x8000,
        All = 0xFFFF,
    };

    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        private static string AppExec = Application.ResourceAssembly.CodeBase.ToString().Replace("file:///", "").Replace("/", "\\");
        private static string AppPath = Path.GetDirectoryName(AppExec);
        private static string AppName = Path.GetFileNameWithoutExtension(AppPath);
        private static string CachePath =  "cache";

        private const string ConvertBGColorKey = "ConvertBGColor";
        private const string ConvertQualityKey = "ConvertQuality";
        private const string ReduceQualityKey = "ReduceQuality";
        private const string AlwaysTopMostKey = "TopMost";
        private Color ConvertBGColor = Properties.Settings.Default.ConvertBGColor;
        private int ConvertQuality = Properties.Settings.Default.ConvertQuality;
        private int ReduceQuality = Properties.Settings.Default.ReduceQuality;
        private bool AlwaysTopMost = Properties.Settings.Default.TopMost;

        private static Configuration config = ConfigurationManager.OpenExeConfiguration(AppExec);
        private static AppSettingsSection appSection = config.AppSettings;

        private static bool SystemEndianLSB = BitConverter.IsLittleEndian ? true : false;

        private string DefaultTitle = null;
        private string[] LineBreak = new string[] { Environment.NewLine, "\r\n", "\n\r", "\n", "\r" };

        //private static string Symbol_Rating_Star_Empty = "\uE8D9";
        private static string Symbol_Rating_Star_Outline = "\uE1CE";
        private static string Symbol_Rating_Star_Filled = "\uE1CF";
        private int _CurrentMetaRating_ = 0;
        public int CurrentMetaRating
        {
            get { return (_CurrentMetaRating_); }
            set
            {
                _CurrentMetaRating_ = value;
                MetaInputRanking1Text.Text = _CurrentMetaRating_ >= 01 ? Symbol_Rating_Star_Filled : Symbol_Rating_Star_Outline;
                MetaInputRanking2Text.Text = _CurrentMetaRating_ >= 20 ? Symbol_Rating_Star_Filled : Symbol_Rating_Star_Outline;
                MetaInputRanking3Text.Text = _CurrentMetaRating_ >= 40 ? Symbol_Rating_Star_Filled : Symbol_Rating_Star_Outline;
                MetaInputRanking4Text.Text = _CurrentMetaRating_ >= 60 ? Symbol_Rating_Star_Filled : Symbol_Rating_Star_Outline;
                MetaInputRanking5Text.Text = _CurrentMetaRating_ >= 80 ? Symbol_Rating_Star_Filled : Symbol_Rating_Star_Outline;
            }
        }

        private static Encoding DBCS = Encoding.GetEncoding("GB18030");
        private static Encoding UTF8 = Encoding.UTF8;
        private static Encoding UNICODE = Encoding.Unicode;

        private static List<string> SupportedFormats { get; set; } = null;

        #region Config Helper
        private string GetConfigValue(string key, object value = null)
        {
            string result = string.Empty;
            if (appSection is AppSettingsSection)
            {
                if (appSection.Settings.AllKeys.Contains(key))
                {
                    result = appSection.Settings[key].Value;
                    if (string.IsNullOrEmpty(result)) result = value.ToString();
                }
                else
                {
                    if (value != null)
                    {
                        appSection.Settings.Add(key, value.ToString());
                        config.Save();
                    }
                }
            }
            return (result);
        }

        private void SetConfigValue(string key, object value = null)
        {
            if (appSection is AppSettingsSection)
            {
                if (value != null)
                {
                    if (appSection.Settings.AllKeys.Contains(key))
                        appSection.Settings[key].Value = value.ToString();
                    else
                        appSection.Settings.Add(key, value.ToString());
                }
                else if (appSection.Settings.AllKeys.Contains(key))
                    appSection.Settings.Remove(key);

                config.Save();
            }
        }
        #endregion

        #region DoEvent Helper
        private static object ExitFrame(object state)
        {
            ((DispatcherFrame)state).Continue = false;
            return null;
        }

        private static SemaphoreSlim CanDoEvents = new SemaphoreSlim(1, 1);
        public static async void DoEvents()
        {
            if (await CanDoEvents.WaitAsync(0))
            {
                try
                {
                    if (Application.Current.Dispatcher.CheckAccess())
                    {
                        await Dispatcher.Yield(DispatcherPriority.Render);
                        //await System.Windows.Threading.Dispatcher.Yield();

                        //DispatcherFrame frame = new DispatcherFrame();
                        //await Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Render, new DispatcherOperationCallback(ExitFrame), frame);
                        //Dispatcher.PushFrame(frame);
                    }
                }
                catch (Exception)
                {
                    try
                    {
                        if (Application.Current.Dispatcher.CheckAccess())
                        {
                            DispatcherFrame frame = new DispatcherFrame();
                            //Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Render, new Action(delegate { }));
                            //Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Send, new Action(delegate { }));

                            //await Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new DispatcherOperationCallback(ExitFrame), frame);
                            //await Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Render, new DispatcherOperationCallback(ExitFrame), frame);
                            await Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Send, new DispatcherOperationCallback(ExitFrame), frame);
                            Dispatcher.PushFrame(frame);
                        }
                    }
                    catch (Exception)
                    {
                        await Task.Delay(1);
                    }
                }
                finally
                {
                    //CanDoEvents.Release(max: 1);
                    if (CanDoEvents is SemaphoreSlim && CanDoEvents.CurrentCount <= 0) CanDoEvents.Release();
                }
            }
        }
        #endregion

        #region Log/MessageBox helper
        private static List<string> _log_ = new List<string>();
        private static void Log(string text)
        {
            try
            {
#if DEBUG
                Debug.WriteLine(text);
#else
                //_log_.Add(text);
#endif
                _log_.Add(text.TrimEnd('\0'));
            }
            catch (Exception ex) { ShowMessage(ex.Message, "ERROR"); }
        }

        /// <summary>
        ///   Sends the specified key.
        /// </summary>
        /// <param name="key">The key.</param>
        public static void Send(Key key)
        {
            if (Keyboard.PrimaryDevice != null)
            {
                if (Keyboard.PrimaryDevice.ActiveSource != null)
                {
                    var e = new KeyEventArgs(Keyboard.PrimaryDevice, Keyboard.PrimaryDevice.ActiveSource, 0, key)
                    {
                        RoutedEvent = Keyboard.KeyDownEvent
                    };
                    InputManager.Current.ProcessInput(e);

                    // Note: Based on your requirements you may also need to fire events for:
                    // RoutedEvent = Keyboard.PreviewKeyDownEvent
                    // RoutedEvent = Keyboard.KeyUpEvent
                    // RoutedEvent = Keyboard.PreviewKeyUpEvent
                }
            }
        }

        private static void ClearLog()
        {
            //Application.Current.Dispatcher.Invoke(() =>
            //{
            try { _log_.Clear(); }
            catch (Exception ex) { ShowMessage(ex.Message, "ERROR"); }
            //});
        }

        private static void ShowLog()
        {
            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var contents_style = (Style)Application.Current.FindResource("MessageDialogWidth");
                var contents = string.Join(Environment.NewLine, _log_.Skip(_log_.Count-500));
                var dlg = new Xceed.Wpf.Toolkit.MessageBox() { };
                dlg.Resources.Add(typeof(TextBlock), contents_style);
                dlg.CaptionIcon = Application.Current.MainWindow.Icon;
                dlg.Language = System.Windows.Markup.XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag);
                dlg.FontFamily = Application.Current.FindResource("MonoSpaceFamily") as FontFamily;
                dlg.Text = contents;
                dlg.Caption = "Metadata Info";
                dlg.Width = 720;
                dlg.MaxWidth = 800;
                dlg.MaxHeight = 480;
                dlg.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                dlg.MouseDoubleClick += (o, e) => { Clipboard.SetText(dlg.Text.Replace("\0", string.Empty).TrimEnd('\0')); };
                dlg.PreviewMouseDown += (o, e) => { if (e.MiddleButton == MouseButtonState.Pressed) Send(Key.Escape); };
                Application.Current.MainWindow.Activate();
                dlg.ShowDialog();
            });
        }

        private static void ShowMessage(string text)
        {
            Xceed.Wpf.Toolkit.MessageBox.Show(Application.Current.MainWindow, text);
        }

        private static void ShowMessage(string text, string title, double? width = null)
        {
            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var contents_style = (Style)Application.Current.FindResource("MessageDialogWidth");
                var dlg = new Xceed.Wpf.Toolkit.MessageBox() { };
                dlg.Resources.Add(typeof(TextBlock), contents_style);
                dlg.CaptionIcon = Application.Current.MainWindow.Icon;
                dlg.Language = System.Windows.Markup.XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag);
                dlg.FontFamily = Application.Current.FindResource("MonoSpaceFamily") as FontFamily;
                dlg.Text = text;
                dlg.Caption = title;
                if (width.HasValue)
                {
                    //dlg.Width = width.Value;
                    dlg.MaxWidth = width.Value;
                }
                dlg.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                dlg.MouseDoubleClick += (o, e) => { Clipboard.SetText(dlg.Text.Replace("\0", string.Empty).TrimEnd('\0')); };
                dlg.PreviewMouseDown += (o, e) => { if (e.MiddleButton == MouseButtonState.Pressed) Send(Key.Escape); };
                Application.Current.MainWindow.Activate();
                dlg.ShowDialog();
            });
        }

        private static bool ConfirmToAll { get; set; } = false;
        private static bool ConfirmNoToAll { get; set; } = false;
        private static bool ConfirmYesToAll { get; set; } = false;
        private static Func<string, string, MessageBoxResult> ShowConfirmFunc = (content, caption) =>
        {
            ConfirmToAll = false;
            ConfirmNoToAll = false;
            ConfirmYesToAll = false;
            content = $"{content}{Environment.NewLine}{Environment.NewLine}[Click Button with SHIFT will Apply To All!]";
            var ret = Xceed.Wpf.Toolkit.MessageBox.Show(Application.Current.MainWindow, content, caption, MessageBoxButton.YesNo, MessageBoxImage.Question);
            ConfirmToAll = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? true : false;
            return(ret);
        };

        private static bool ShowConfirm(string text, string title)
        {
            var result = false;

            if (ConfirmNoToAll) return (false);
            if (ConfirmYesToAll) return (true);

            var ret = Application.Current.Dispatcher.Invoke(ShowConfirmFunc, text, title);
            result = ret is MessageBoxResult && (MessageBoxResult)ret == MessageBoxResult.Yes ? true : false;
            if (ConfirmToAll)
            {
                ConfirmYesToAll = result;
                ConfirmNoToAll = !result;
            }

            return (result);
        }

        private string ShowCustomForm(UIElement form, string input, string title = null)
        {
            var result = input;
            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                //var dlg = new Xceed.Wpf.Toolkit.MessageBox();
                //dlg.CaptionIcon = Application.Current.MainWindow.Icon;
                //dlg.Language = System.Windows.Markup.XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag);
                //dlg.FontFamily = Application.Current.FindResource("MonoSpaceFamily") as FontFamily;
                //dlg.Caption = "Metadata Info";
                //dlg.Content = form;
                //dlg.MaxWidth = 720;
                //dlg.MaxHeight = 480;
                //dlg.HorizontalContentAlignment = HorizontalAlignment.Stretch;

                var dlg = new Xceed.Wpf.Toolkit.CollectionControlDialog();
                dlg.Icon = Application.Current.MainWindow.Icon;

                dlg.Language = System.Windows.Markup.XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag);
                dlg.FontFamily = Application.Current.FindResource("MonoSpaceFamily") as FontFamily;
                dlg.Title = string.IsNullOrEmpty(title) ? "Metadata Info" : title;
                dlg.Content = form;
                dlg.MaxWidth = 720;
                dlg.MaxHeight = 480;
                dlg.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                //dlg.MouseDoubleClick += (o, e) => { Clipboard.SetText(dlg.Text.Replace("\0", string.Empty).TrimEnd('\0')); };
                //dlg.PreviewMouseDown += (o, e) => { if (e.MiddleButton == MouseButtonState.Pressed) t };
                Application.Current.MainWindow.Activate();
                dlg.ShowDialog();
            });
            return (result);
        }
        #endregion

        #region Background Worker Helper
        private IProgress<KeyValuePair<double, string>> progress = null;
        private Action<double, double, string> ReportProgress = null;
        private BackgroundWorker bgWorker = null;
        private int ExtendedMessageWidth = 106;
        private int NormallyMessageWidth = 75;
        private void ProgressReset()
        {
            Dispatcher.InvokeAsync(() => { Progress.Value = 0; });
        }

        private void ProgressReport(double count, double total, string tooltip)
        {
            if (ReportProgress is Action<double, double, string>) ReportProgress.Invoke(count, total, tooltip);
        }

        private void RunBgWorker(Action<string, bool> action, bool showlog = true)
        {
            ConfirmToAll = false;
            ConfirmNoToAll = false;
            ConfirmYesToAll = false;

            if (action is Action<string, bool> && bgWorker is BackgroundWorker && !bgWorker.IsBusy)
            {
                IList<string> files = GetFiles(FilesList);
                var selected_file = FilesList.SelectedItem != null ? FilesList.SelectedItem as string : string.Empty;
                if (files.Count > 0)
                {
                    bgWorker.RunWorkerAsync(new Action(() =>
                    {
                        ClearLog();
                        ProgressReset();
                        double count = 0;
                        foreach (var file in files)
                        {
                            ProgressReport(count, files.Count, file);
                            Log($"{file}");
                            Log("-".PadRight(ExtendedMessageWidth, '-'));
                            if (File.Exists(file)) action.Invoke(file, files.Count == 1);
                            if (file.Equals(selected_file, StringComparison.CurrentCultureIgnoreCase))
                            {
                                UpdateFileTimeInfo(file);
                            }
                            Log("=".PadRight(ExtendedMessageWidth, '='));
                            ProgressReport(++count, files.Count, file);
                        }
                        if (showlog) ShowLog();
                    }));
                }
            }
        }

        private void BgWorker_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            if (ReportProgress is Action<double, double, string>) ReportProgress.Invoke(e.ProgressPercentage, 0, "");
        }

        private void BgWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            //Progress.Value = 100;
        }

        private void BgWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            if (e.Argument is Action)
            {
                var action = e.Argument as Action;
                action.Invoke();
            }
        }

        private void InitBgWorker()
        {
            if (bgWorker == null)
            {
                bgWorker = new BackgroundWorker() { WorkerReportsProgress = true, WorkerSupportsCancellation = true };
                bgWorker.DoWork += BgWorker_DoWork;
                bgWorker.RunWorkerCompleted += BgWorker_RunWorkerCompleted;
                bgWorker.ProgressChanged += BgWorker_ProgressChanged;
            }
            if (progress == null)
            {
                progress = new Progress<KeyValuePair<double, string>>(kv =>
                {
                    try
                    {
                        var k = kv.Key;
                        var v = kv.Value;
                        Progress.Value = k * 100;
                        if (k >= 1) Progress.ToolTip = $"100% : {v}";
                        else if (k <= 0) Progress.ToolTip = $"0% : {v}";
                        else Progress.ToolTip = $"{k:P1} : {v}";
                    }
                    catch { }
                });
            }
            if (ReportProgress == null)
            {
                Progress.Minimum = 0;
                Progress.Maximum = 100;
                Progress.Value = 0;
                ReportProgress = new Action<double, double, string>((count, total, tooltip) =>
                {
                    Dispatcher.InvokeAsync(async () =>
                    {
                        try
                        {
                            var percent = total <= 0 ? count : count / total;
                            Progress.Value = percent * 100;
                            if (percent >= 1) Progress.ToolTip = $"100% : {tooltip}";
                            else if (percent <= 0) Progress.ToolTip = $"0% : {tooltip}";
                            else Progress.ToolTip = $"{percent:P1} : {tooltip}";

                            if (percent >= 1 || percent <= 0) Title = DefaultTitle;
                            else SetTitle($"[{percent:P1} - {count}/{total}]");
                        }
                        catch { }

                        await Task.Delay(1);
                        DoEvents();
                    });
                });
            }
        }
        #endregion

        #region Get/Set Datetime Helper
        private void SetCustomDateTime(DateTime? dt = null, DateTime? dtc = null, DateTime? dtm = null, DateTime? dta = null)
        {
            Dispatcher.InvokeAsync(() =>
            {
                DateCreated.SelectedDate = dtc ?? dt ?? DateCreated.SelectedDate;
                DateModified.SelectedDate = dtm ?? dt ?? DateModified.SelectedDate;
                DateAccessed.SelectedDate = dta ?? dt ?? DateAccessed.SelectedDate;

                TimeCreated.Value = dtc ?? dt ?? TimeCreated.Value;
                TimeModified.Value = dtm ?? dt ?? TimeModified.Value;
                TimeAccessed.Value = dta ?? dt ?? TimeAccessed.Value;

                UpdateFileTimeInfo();
            });
        }

        private DateTime? GetCustomDateTime(FrameworkElement element_date, FrameworkElement element_time)
        {
            DateTime? result = null;
            if (element_date is DatePicker && element_time is Xceed.Wpf.Toolkit.TimePicker)
            {
                var date = element_date as DatePicker;
                var time = element_time as Xceed.Wpf.Toolkit.TimePicker;

                var d_value = date.SelectedDate ?? time.Value ?? DateTime.Now;
                var t_value = time.Value ?? date.SelectedDate ?? DateTime.Now;
                result = d_value.Date + t_value.TimeOfDay;
            }
            return (result);
        }
        #endregion

        #region Files List Opration Helper
        private static string SmartFileSize(long size)
        {
            var result = size.ToString("#,0 B");
            if (size >= 100000000) result = (size / 1000000D).ToString("0.# MB");
            else if (size >= 1000000) result = (size / 1000000D).ToString("0.## MB");
            else if (size >= 100000) result = (size / 1000D).ToString("0.# kB");
            else if (size >= 10000) result = (size / 1000D).ToString("0.## kB");
            return (result);
        }

        private bool LoadFiles(IEnumerable<string> files)
        {
            var result = false;
            try
            {
                var flist = new List<string>();
                foreach (var file in files)
                {
                    flist.AddRange(Directory.GetFileSystemEntries(Path.IsPathRooted(file) ? Path.GetDirectoryName(file) : Directory.GetCurrentDirectory(), Path.GetFileName(file), SearchOption.TopDirectoryOnly));
                }

                foreach (var file in flist)
                {
                    if (Directory.Exists(file))
                    {
                        var fs = Directory.EnumerateFiles(file);
                        foreach (var f in fs) if (FilesList.Items.IndexOf(f) < 0) FilesList.Items.Add(f);
                    }
                    else if (FilesList.Items.IndexOf(file) < 0) FilesList.Items.Add(file);
                }
                result = true;
            }
            catch (Exception ex) { ShowMessage(ex.Message, "ERROR"); }
            finally { SetTitle(); }
            return (result);
        }

        public bool LoadFiles()
        {
            var result = false;
            try
            {
                var dlgOpen = new Microsoft.Win32.OpenFileDialog() { Multiselect = true, CheckFileExists = true, CheckPathExists = true, ValidateNames = true };
                dlgOpen.Filter = "All Files|*.*";
                if (dlgOpen.ShowDialog() ?? false)
                {
                    var files = dlgOpen.FileNames;
                    result = new Func<bool>(() => { return (LoadFiles(files)); }).Invoke();
                }
            }
            catch (Exception ex) { ShowMessage(ex.Message, "ERROR"); }
            return (result);
        }

        private void UpdateFileTimeInfo(string file = null)
        {
            Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    if (string.IsNullOrEmpty(file))
                    {
                        if (FilesList.SelectedItem != null)
                        {
                            file = FilesList.SelectedItem as string;
                            UpdateFileTimeInfo(file);
                        }
                    }
                    else if (File.Exists(file))
                    {
                        var fi = new FileInfo(file);

                        List<string> info = new List<string>();
                        info.Add($"Created  Time : {fi.CreationTime.ToString()} => {DateCreated.SelectedDate}");
                        info.Add($"Modified Time : {fi.LastWriteTime.ToString()} => {DateModified.SelectedDate}");
                        info.Add($"Accessed Time : {fi.LastAccessTime.ToString()} => {DateAccessed.SelectedDate}");
                        FileTimeInfo.Text = string.Join(Environment.NewLine, info);
                    }

                }
                catch (Exception ex) { ShowMessage(ex.Message, "ERROR"); }
            });
        }

        private void AddFile(string file)
        {
            Dispatcher.InvokeAsync(() =>
            {
                var idx = FilesList.Items.IndexOf(file);
                if (idx >= 0) FilesList.Items.RemoveAt(idx);
                FilesList.Items.Add(file);
            });
        }

        private Func<ListBox, IList<string>> GetFiles = (element)=>
        {
            List<string> files = new List<string>();
            if (element is ListBox && element.Items.Count > 0)
            {
                element.Dispatcher.Invoke(() =>
                {
                    foreach (var item in element.SelectedItems.Count > 0 ? element.SelectedItems : element.Items) files.Add(item as string);
                });
            }
            return(files);
        };

        private IList<string> GetSelected()
        {
            List<string> files = new List<string>();
            if (FilesList.Items.Count >= 1)
            {
                Dispatcher.InvokeAsync(() =>
                {
                    foreach (var item in FilesList.SelectedItems.Count > 0 ? FilesList.SelectedItems : FilesList.Items) files.Add(item as string);
                });
            }
            return (files);
        }
        #endregion

        #region Text/Color Converting Helper
        private static int RatingToRanking(int rating)
        {
            var ranking = 0;
            try
            {
                if (rating >= 99) ranking = 5;
                else if (rating >= 75) ranking = 4;
                else if (rating >= 50) ranking = 3;
                else if (rating >= 25) ranking = 2;
                else if (rating >= 01) ranking = 1;
            }
            catch { }
            return (ranking);
        }

        private static int RatingToRanking(int? rating)
        {
            return (RatingToRanking(rating ?? 0));
        }

        private static int RankingToRating(int ranking)
        {
            var rating = 0;
            try
            {
                if (ranking >= 5) rating = 99;
                else if (ranking >= 4) rating = 75;
                else if (ranking >= 3) rating = 50;
                else if (ranking >= 2) rating = 25;
                else if (ranking >= 1) rating = 01;
            }
            catch { }
            return (rating);
        }

        private static int RankingToRating(int? ranking)
        {
            return (RankingToRating(ranking ?? 0));
        }

        private static MagickColor XYZ2RGB(double x, double y, double z)
        {
            var r =  3.2410 * x + -1.5374 * y + -0.4986 * z;
            var g = -0.9692 * x +  1.8760 * y +  0.0416 * z;
            var b =  0.0556 * x + -0.2040 * y +  1.0570 * z;
            if (r <= 0.00304) r = 12.92 * r;
            else r = (1 + 0.055) * Math.Pow(r, 1 / 2.4) - 0.055;
            if (g <= 0.00304) g = 12.92 * g;
            else g = (1 + 0.055) * Math.Pow(g, 1 / 2.4) - 0.055;
            if (b <= 0.00304) b = 12.92 * b;
            else b = (1 + 0.055) * Math.Pow(b, 1 / 2.4) - 0.055;

            Color c = Color.FromScRgb(1, (float)r, (float)g, (float)b);
            return (MagickColor.FromRgba(c.R, c.G, c.B, c.A));
        }

        private static string BytesToString(byte[] bytes, bool ascii = false, bool msb = false, Encoding encoding = null)
        {
            var result = string.Empty;
            if (bytes is byte[] && bytes.Length > 0)
            {
                if (ascii) result = Encoding.ASCII.GetString(bytes);
                else
                {
                    if (bytes.Length > 8)
                    {
                        var idcode_bytes = bytes.Take(8).ToArray();
                        var idcode_name = Encoding.ASCII.GetString(idcode_bytes).TrimEnd().TrimEnd('\0').TrimEnd();
                        if ("UNICODE".Equals(idcode_name, StringComparison.CurrentCultureIgnoreCase))
                        {
                            if (msb)
                                result = Encoding.BigEndianUnicode.GetString(bytes.Skip(8).ToArray());
                            else
                                result = Encoding.Unicode.GetString(bytes.Skip(8).ToArray());
                        }
                        else if ("Ascii".Equals(idcode_name, StringComparison.CurrentCultureIgnoreCase))
                        {
                            result = Encoding.ASCII.GetString(bytes.Skip(8).ToArray());
                        }
                        else if ("Default".Equals(idcode_name, StringComparison.CurrentCultureIgnoreCase))
                        {
                            result = Encoding.Default.GetString(bytes.Skip(8).ToArray());
                        }
                        else if (idcode_bytes.Where(b => b == 0).Count() == 8)
                        {
                            result = Encoding.Default.GetString(bytes.Skip(8).ToArray());
                        }
                    }

                    if (string.IsNullOrEmpty(result))
                    {
                        var bytes_text = bytes.Select(c => ascii ? $"{Convert.ToChar(c)}" : $"{c}");
                        if (encoding == null)
                        {
                            result = string.Join(", ", bytes_text);
                            if (bytes.Length > 4)
                            {
                                var text = BytesToUnicode(result);
                                if (!result.StartsWith("0,") && !string.IsNullOrEmpty(text)) result = text;
                            }
                        }
                        else result = encoding.GetString(bytes);
                    }
                }
            }
            return (result);
        }

        private static string BytesToUnicode(byte[] bytes, bool ascii = false, bool msb = false)
        {
            var result = string.Empty;
            if (bytes is byte[] && bytes.Length > 0)
            {
                result = ascii ? Encoding.ASCII.GetString(bytes) : (msb ? Encoding.BigEndianUnicode.GetString(bytes) : Encoding.Unicode.GetString(bytes));
            }
            return (result);
        }

        private static string BytesToUnicode(string text, bool msb = false)
        {
            var result = text;
            if (!string.IsNullOrEmpty(text))
            {
                foreach (Match m in Regex.Matches($"{text},", @"((\d{1,3}, ?){2,})"))
                {
                    List<byte> bytes = new List<byte>();
                    var values = m.Groups[1].Value.Split(',').Select(s => s.Trim()).ToList();
                    foreach (var value in values)
                    {
                        if (string.IsNullOrEmpty(value) || int.Parse(value) > 255) continue;
                        bytes.Add(byte.Parse(value));
                    }
                    if (bytes.Count > 0) result = result.Replace(m.Groups[1].Value.Trim(','), msb ? Encoding.BigEndianUnicode.GetString(bytes.ToArray()) : Encoding.Unicode.GetString(bytes.ToArray()));//.TrimEnd('\0'));
                }
            }
            return (result);
        }

        private static byte[] ByteStringToBytes(string text)
        {
            byte[] result = null;
            if (!string.IsNullOrEmpty(text))
            {
                List<byte> bytes = new List<byte>();
                foreach (Match m in Regex.Matches($"{text.TrimEnd().TrimEnd(',')},", @"((\d{1,3}) ?, ?)"))
                {
                    var value = m.Groups[2].Value.Trim().TrimEnd(',');
                    if (int.Parse(value) > 255) continue;
                    bytes.Add(byte.Parse(value));
                }
                result = bytes.ToArray();
            }
            return (result);
        }

        private static string ByteStringToString(string text, Encoding encoding = default(Encoding), bool msb = false)
        {
            if (encoding == null) encoding = Encoding.UTF8;
            if (msb && encoding == Encoding.Unicode) encoding = Encoding.BigEndianUnicode;
            return (encoding.GetString(ByteStringToBytes(text)));
        }

        private static string UnicodeToBytes(string text)
        {
            var result = string.Empty;
            if (!string.IsNullOrEmpty(text))
            {
                var bytes = Encoding.Unicode.GetBytes(text);
                var bytes_text = bytes.Select(b => $"{b}");
                result = string.Join(", ", bytes_text);
            }
            return (result);
        }

        private static string UnicodeToUtf8(string text)
        {
            return (UTF8.GetString(UNICODE.GetBytes(text)));
        }

        private static string Utf8ToUnicode(string text)
        {
            return (UNICODE.GetString(UTF8.GetBytes(text)));
        }

        private static string DbcsToUtf8(string text)
        {
            return (UTF8.GetString(DBCS.GetBytes(text)));
        }

        private static string Utf8ToDbcs(string text)
        {
            return (DBCS.GetString(UTF8.GetBytes(text)));
        }

        private static double VALUE_GB = 1024 * 1024 * 1024;
        private static double VALUE_MB = 1024 * 1024;
        private static double VALUE_KB = 1024;

        private static string SmartFileSize(long v, double factor = 1, bool unit = true, int padleft = 0) { return (SmartFileSize((double)v, factor, unit, padleft: padleft)); }

        private static string SmartFileSize(double v, double factor = 1, bool unit = true, bool trimzero = true, int padleft = 0)
        {
            string v_str = string.Empty;
            string u_str = string.Empty;
            if (double.IsNaN(v) || double.IsInfinity(v) || double.IsNegativeInfinity(v) || double.IsPositiveInfinity(v)) { v_str = "0"; u_str = "B"; }
            else if (v >= VALUE_GB) { v_str = $"{v / factor / VALUE_GB:F2}"; u_str = "GB"; }
            else if (v >= VALUE_MB) { v_str = $"{v / factor / VALUE_MB:F2}"; u_str = "MB"; }
            else if (v >= VALUE_KB) { v_str = $"{v / factor / VALUE_KB:F2}"; u_str = "KB"; }
            else { v_str = $"{v / factor:F0}"; u_str = "B"; }
            var vs = trimzero && !u_str.Equals("B") ? v_str.Trim('0').TrimEnd('.') : v_str;
            return ((unit ? $"{vs} {u_str}" : vs).PadLeft(padleft));
        }

        private static char[] DateTimeTrimSymbols = new char[] {
            ' ', '·',
            '`', '~', '!', '@', '#', '$', '%', '^', '&', '*', ':', ';', '?', ',', '.', '+', '-', '_',
            '！', '＠', '＃', '＄', '％', '＾', '＆', '＊', '～',  '。', '，', '；', '：', '＇', '？', '，', '．', '＝', '－', '＿', '＋',
            '|', '\'', '/', '＼', '／', '｜',
            '<', '>', '(', ')', '[', ']', '{', '}', '＜', '＞', '（', '）', '【', '】', '｛', '｝', '「', '」',
            '"', '＂', '“', '”'
        };

        private static string NormalizeDateTimeText(string text)
        {
            var AM = CultureInfo.CurrentCulture.DateTimeFormat.AMDesignator;
            var PM = CultureInfo.CurrentCulture.DateTimeFormat.PMDesignator;
            var result = text;
            try
            {
                result = Regex.Replace(result, @"号|號|日", "日 ", RegexOptions.IgnoreCase);
                result = Regex.Replace(result, @"点|點|時|时", "时 ", RegexOptions.IgnoreCase);
                result = Regex.Replace(result, $@"早 *?上|午 *?前|{AM}|AM", $"{AM} ", RegexOptions.IgnoreCase);
                result = Regex.Replace(result, $@"晚 *?上|午 *?後|{PM}|PM", $"{PM} ", RegexOptions.IgnoreCase);
                result = Regex.Replace(result, @"[·](Twitter|Tweet).*?$", "", RegexOptions.IgnoreCase);
                result = Regex.Replace(result, @"[·]", " ", RegexOptions.IgnoreCase);
                result = result.Trim(DateTimeTrimSymbols);

                DateTime dt;
                if (DateTime.TryParse(result, out dt)) result = dt.ToString();
            }
            catch (Exception ex) { Log(ex.Message); }
            return (result.Trim());
        }
        #endregion

        #region XML Formating Helper
        private static List<string> xmp_ns = new List<string> { "rdf", "xmp", "dc", "exif", "tiff", "iptc", "MicrosoftPhoto" };
        private static Dictionary<string, string> xmp_ns_lookup = new Dictionary<string, string>()
        {
            {"rdf", "http://www.w3.org/1999/02/22-rdf-syntax-ns#" },
            {"xmp", "http://ns.adobe.com/xap/1.0/" },
            {"dc", "http://purl.org/dc/elements/1.1/" },
            //{"iptc", "http://ns.adobe.com/iptc/1.0/" },
            {"exif", "http://ns.adobe.com/exif/1.0/" },
            {"tiff", "http://ns.adobe.com/tiff/1.0/" },
            {"photoshop", "http://ns.adobe.com/photoshop/1.0/" },
            {"MicrosoftPhoto", "http://ns.microsoft.com/photo/1.0" },
            //{"MicrosoftPhoto", "http://ns.microsoft.com/photo/1.0/" },
            //{"MicrosoftPhoto", "http://ns.microsoft.com/photo/1.2/" },
        };

        private static string FormatXML(string xml)
        {
            var result = xml;
            try
            {
                var doc = new XmlDocument();
                doc.LoadXml(xml);
                result = FormatXML(doc);
            }
            catch (Exception ex) { ShowMessage(ex.Message, "ERROR"); }
            return (result);
        }

        private static string FormatXML(XmlDocument xml)
        {
            var result = xml.OuterXml;
            using (var ms = new MemoryStream())
            {
                var writer = new XmlTextWriter(ms, Encoding.UTF8);
                writer.Formatting = Formatting.Indented;
                xml.WriteContentTo(writer);
                writer.Flush();
                ms.Flush();
                ms.Seek(0, SeekOrigin.Begin);
                using (var sr = new StreamReader(ms)) { result = sr.ReadToEnd(); }
                result = result.Replace("\"", "'");
                foreach (var ns in xmp_ns) { result = result.Replace($" xmlns:{ns}='{ns}'", ""); }
            }
            return (result);
        }

        private static string FormatXML(XmlNode xml)
        {
            var result = xml.OuterXml;
            using (var ms = new MemoryStream())
            {
                var writer = new XmlTextWriter(ms, Encoding.UTF8);
                writer.Formatting = Formatting.Indented;
                xml.WriteTo(writer);
                writer.Flush();
                ms.Flush();
                ms.Seek(0, SeekOrigin.Begin);
                using (var sr = new StreamReader(ms)) { result = sr.ReadToEnd(); }
            }
            return (result);
        }

        private static string FormatXML(XmlElement xml)
        {
            var result = xml.OuterXml;
            using (var ms = new MemoryStream())
            {
                var writer = new XmlTextWriter(ms, Encoding.UTF8);
                writer.Formatting = Formatting.Indented;
                xml.WriteTo(writer);
                writer.Flush();
                ms.Flush();
                ms.Seek(0, SeekOrigin.Begin);
                using (var sr = new StreamReader(ms)) { result = sr.ReadToEnd(); }
            }
            return (result);
        }

        private static string FormatXML(XmlDocument xml, bool merge_nodes)
        {
            var result = FormatXML(xml);
            if (merge_nodes && xml is XmlDocument)
            {
                foreach (XmlElement root in xml.DocumentElement.ChildNodes)
                {
                    var elements_list = new Dictionary<string, XmlElement>();
                    Func<XmlElement, IList<XmlElement>> ChildList = (elements)=>
                    {
                        var list = new List<XmlElement>();
                        foreach(XmlElement node in elements.ChildNodes) list.Add(node);
                        return(list);
                    };
                    foreach (XmlElement node in ChildList.Invoke(root))
                    {
                        foreach (XmlAttribute attr in node.Attributes)
                        {
                            if (attr.Name.StartsWith("xmlns:"))
                            {
                                var key = attr.Name.Substring(6);
                                if (xmp_ns_lookup.ContainsKey(key))
                                {
                                    if (!elements_list.ContainsKey(key) || elements_list[key] == null)
                                    {
                                        elements_list[key] = xml.CreateElement("rdf:Description", "rdf");
                                        elements_list[key].SetAttribute($"xmlns:{key}", xmp_ns_lookup[key]);
                                    }
                                }
                                else
                                {
                                    if (!elements_list.ContainsKey(key) || elements_list[key] == null)
                                    {
                                        elements_list[key] = xml.CreateElement("rdf:Description", "rdf");
                                        elements_list[key].SetAttribute($"xmlns:{key}", attr.Value);
                                    }
                                }
                                if (!xmp_ns.Contains(key)) xmp_ns.Append(key);
                            }
                            else
                            {
                                var keys = attr.Name.Split(':');
                                var key = keys[0];
                                var xmlns = $"xmlns:{key}";
                                if (node.HasAttribute(xmlns))
                                {
                                    var value = node.GetAttribute(xmlns);
                                    if (!elements_list.ContainsKey(key) || elements_list[key] == null)
                                    {
                                        elements_list[key] = xml.CreateElement("rdf:Description", "rdf");
                                        elements_list[key].SetAttribute($"xmlns:{key}", value);
                                    }
                                    if (!xmp_ns.Contains(key)) xmp_ns.Append(key);
                                    var child = xml.CreateElement(attr.Name, key);
                                    child.InnerText = attr.Value;
                                    elements_list[key].AppendChild(child);
                                }
                            }
                        }

                        try
                        {
                            foreach (XmlElement item in ChildList.Invoke(node))
                            {
                                var xmlns = $"xmlns:{item.Prefix}";
                                var ns = item.NamespaceURI;
                                if (item.HasAttribute(xmlns))
                                {
                                    if (!xmp_ns_lookup.ContainsKey(item.Prefix)) { xmp_ns_lookup.Add(item.Prefix, item.GetAttribute(xmlns)); }
                                    if (!elements_list.ContainsKey(item.Prefix) || elements_list[item.Prefix] == null)
                                    {
                                        elements_list[item.Prefix] = xml.CreateElement("rdf:Description", "rdf");
                                        elements_list[item.Prefix].SetAttribute($"xmlns:{item.Prefix}", xmp_ns_lookup[item.Prefix]);
                                    }
                                    item.RemoveAttribute(xmlns);
                                }
                                else
                                {
                                    if (!xmp_ns_lookup.ContainsKey(item.Prefix)) { xmp_ns_lookup.Add(item.Prefix, ns); }
                                    if (!elements_list.ContainsKey(item.Prefix) || elements_list[item.Prefix] == null)
                                    {
                                        elements_list[item.Prefix] = xml.CreateElement("rdf:Description", "rdf");
                                        elements_list[item.Prefix].SetAttribute($"xmlns:{item.Prefix}", xmp_ns_lookup[item.Prefix]);
                                    }
                                }
                                elements_list[item.Prefix].AppendChild(item);
                            }
                            root.RemoveChild(node);
                        }
                        catch (Exception ex) { Log(ex.Message); }
                    }
                    foreach (var kv in elements_list) { if (kv.Value is XmlElement && kv.Value.HasChildNodes) root.AppendChild(kv.Value); }
                }
                result = FormatXML(xml);
            }
            return (result);
        }

        private static string FormatXML(string xml, bool merge_nodes)
        {
            var result = xml;
            if (!string.IsNullOrEmpty(xml))
            {
                XmlDocument xml_doc = new XmlDocument();
                xml_doc.LoadXml(xml);
                result = FormatXML(xml_doc, merge_nodes);
            }
            return (result);
        }

        private static string TouchXMP(string xml, FileInfo fi, MetaInfo meta)
        {
            if (meta is MetaInfo && fi is FileInfo)
            {
                var title = meta is MetaInfo ? meta.Title ?? Path.GetFileNameWithoutExtension(fi.Name) : Path.GetFileNameWithoutExtension(fi.Name);
                var subject = meta is MetaInfo ? meta.Subject : title;
                var authors = meta is MetaInfo ? meta.Authors : string.Empty;
                var copyright = meta is MetaInfo ? meta.Copyrights : authors;
                var keywords = meta is MetaInfo ? meta.Keywords : string.Empty;
                var comment = meta is MetaInfo ? meta.Comment : string.Empty;
                var rating = meta is MetaInfo ? meta.RatingPercent : null;
                if (!string.IsNullOrEmpty(title)) title.Replace("\0", string.Empty).TrimEnd('\0');
                if (!string.IsNullOrEmpty(subject)) subject.Replace("\0", string.Empty).TrimEnd('\0');
                if (!string.IsNullOrEmpty(authors)) authors.Replace("\0", string.Empty).TrimEnd('\0');
                if (!string.IsNullOrEmpty(copyright)) copyright.Replace("\0", string.Empty).TrimEnd('\0');
                if (!string.IsNullOrEmpty(keywords)) keywords.Replace("\0", string.Empty).TrimEnd('\0');
                if (!string.IsNullOrEmpty(comment)) comment.Replace("\0", string.Empty).TrimEnd('\0');

                #region Init datetime string
                var dc = (meta is MetaInfo ? meta.DateCreated ?? meta.DateAcquired ?? meta.DateTaken : null) ?? fi.CreationTime;
                var dm = (meta is MetaInfo ? meta.DateModified ?? meta.DateAcquired ?? meta.DateTaken : null) ?? fi.LastWriteTime;
                var da = (meta is MetaInfo ? meta.DateAccesed ?? meta.DateAcquired ?? meta.DateTaken : null) ?? fi.LastAccessTime;

                // 2021:09:13 11:00:16
                var dc_exif = dc.ToString("yyyy:MM:dd HH:mm:ss");
                var dm_exif = dm.ToString("yyyy:MM:dd HH:mm:ss");
                var da_exif = da.ToString("yyyy:MM:dd HH:mm:ss");
                // 2021:09:13T11:00:16
                var dc_xmp = dc.ToString("yyyy:MM:dd HH:mm:ss");
                var dm_xmp = dm.ToString("yyyy:MM:dd HH:mm:ss");
                var da_xmp = da.ToString("yyyy:MM:dd HH:mm:ss");
                // 2021-09-13T06:38:49+00:00
                var dc_date = dc.ToString("yyyy-MM-ddTHH:mm:sszzz");
                var dm_date = dm.ToString("yyyy-MM-ddTHH:mm:sszzz");
                var da_date = da.ToString("yyyy-MM-ddTHH:mm:sszzz");
                // 2021-08-26T12:23:49
                var dc_ms = dc.ToString("yyyy-MM-ddTHH:mm:sszzz");
                var dm_ms = dm.ToString("yyyy-MM-ddTHH:mm:sszzz");
                var da_ms = da.ToString("yyyy-MM-ddTHH:mm:sszzz");
                // 2021-08-26T12:23:49.002
                var dc_msxmp = dc.ToString("yyyy-MM-ddTHH:mm:ss.fff");
                var dm_msxmp = dm.ToString("yyyy-MM-ddTHH:mm:ss.fff");
                var da_msxmp = da.ToString("yyyy-MM-ddTHH:mm:ss.fff");
                // 2021-09-13T08:38:13Z
                var dc_png = dc.ToString("yyyy-MM-ddTHH:mm:ssZ");
                var dm_png = dm.ToString("yyyy-MM-ddTHH:mm:ssZ");
                var da_png = da.ToString("yyyy-MM-ddTHH:mm:ssZ");
                // 2021:09:13 11:00:16+08:00
                var dc_misc = dc.ToString("yyyy:MM:dd HH:mm:sszzz");
                var dm_misc = dm.ToString("yyyy:MM:dd HH:mm:sszzz");
                var da_misc = da.ToString("yyyy:MM:dd HH:mm:sszzz");
                #endregion

                #region Normalization Keywords
                var keyword_list = string.IsNullOrEmpty(keywords) ? new List<string>() : keywords.Split(new char[] { ';', '#' }, StringSplitOptions.RemoveEmptyEntries).Select(k => k.Trim()).Where(k => !string.IsNullOrEmpty(k)).Distinct();
                keywords = string.Join("; ", keyword_list);
                #endregion

                #region Init a XMP contents
                if (string.IsNullOrEmpty(xml))
                {
                    //var xml = $"<?xpacket begin='?' id='W5M0MpCehiHzreSzNTczkc9d'?>{Environment.NewLine}<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\"><rdf:Description rdf:about=\"uuid:faf5bdd5-ba3d-11da-ad31-d33d75182f1b\" xmlns:MicrosoftPhoto=\"http://ns.microsoft.com/photo/1.0/\"><MicrosoftPhoto:DateAcquired>{dm_msxmp}</MicrosoftPhoto:DateAcquired></rdf:Description></rdf:RDF></x:xmpmeta>{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}                            <?xpacket end='w'?>";
                    //var xml = $"<?xpacket begin='?' id='W5M0MpCehiHzreSzNTczkc9d'?><x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\"><rdf:Description rdf:about=\"uuid:faf5bdd5-ba3d-11da-ad31-d33d75182f1b\" xmlns:MicrosoftPhoto=\"http://ns.microsoft.com/photo/1.0/\"><MicrosoftPhoto:DateAcquired>{dm_msxmp}</MicrosoftPhoto:DateAcquired><MicrosoftPhoto:DateTaken>{dm_msxmp}</MicrosoftPhoto:DateTaken></rdf:Description><rdf:Description about='' xmlns:exif='http://ns.adobe.com/exif/1.0/'><exif:DateTimeDigitized>{dm_ms}</exif:DateTimeDigitized><exif:DateTimeOriginal>{dm_ms}</exif:DateTimeOriginal></rdf:Description></rdf:RDF></x:xmpmeta><?xpacket end='w'?>";

                    xml = $"<?xpacket begin='?' id='W5M0MpCehiHzreSzNTczkc9d'?><x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\"></rdf:RDF></x:xmpmeta><?xpacket end='w'?>";
                }
                #endregion
                try
                {
                    var xml_doc = new XmlDocument();
                    xml_doc.LoadXml(xml);
                    var root_nodes = xml_doc.GetElementsByTagName("rdf:RDF");
                    if (root_nodes.Count >= 1)
                    {
                        var root_node = root_nodes.Item(0);
                        #region Title node
                        if (xml_doc.GetElementsByTagName("dc:title").Count <= 0)
                        {
                            var desc = xml_doc.CreateElement("rdf:Description", "rdf");
                            desc.SetAttribute("xmlns:dc", xmp_ns_lookup["dc"]);
                            desc.AppendChild(xml_doc.CreateElement("dc:title", "dc"));
                            root_node.AppendChild(desc);
                        }
                        #endregion
                        #region Comment node
                        if (xml_doc.GetElementsByTagName("dc:description").Count <= 0)
                        {
                            var desc = xml_doc.CreateElement("rdf:Description", "rdf");
                            desc.SetAttribute("xmlns:dc", xmp_ns_lookup["dc"]);
                            desc.AppendChild(xml_doc.CreateElement("dc:description", "dc"));
                            root_node.AppendChild(desc);
                        }
                        #endregion
                        #region Author node
                        if (xml_doc.GetElementsByTagName("dc:creator").Count <= 0)
                        {
                            var desc = xml_doc.CreateElement("rdf:Description", "rdf");
                            desc.SetAttribute("xmlns:dc", xmp_ns_lookup["dc"]);
                            desc.AppendChild(xml_doc.CreateElement("dc:creator", "dc"));
                            root_node.AppendChild(desc);
                        }
                        if (xml_doc.GetElementsByTagName("xmp:creator").Count <= 0)
                        {
                            var desc = xml_doc.CreateElement("rdf:Description", "rdf");
                            desc.SetAttribute("xmlns:xmp", xmp_ns_lookup["xmp"]);
                            desc.AppendChild(xml_doc.CreateElement("xmp:creator", "xmp"));
                            root_node.AppendChild(desc);
                        }
                        #endregion
                        #region Keywords node
                        if (xml_doc.GetElementsByTagName("dc:subject").Count <= 0)
                        {
                            var desc = xml_doc.CreateElement("rdf:Description", "rdf");
                            desc.SetAttribute("xmlns:dc", xmp_ns_lookup["dc"]);
                            desc.AppendChild(xml_doc.CreateElement("dc:subject", "dc"));
                            root_node.AppendChild(desc);
                        }
                        if (xml_doc.GetElementsByTagName("MicrosoftPhoto:LastKeywordXMP").Count <= 0)
                        {
                            var desc = xml_doc.CreateElement("rdf:Description", "rdf");
                            desc.SetAttribute("xmlns:MicrosoftPhoto", xmp_ns_lookup["MicrosoftPhoto"]);
                            desc.AppendChild(xml_doc.CreateElement("MicrosoftPhoto:LastKeywordXMP", "MicrosoftPhoto"));
                            root_node.AppendChild(desc);
                        }
                        if (xml_doc.GetElementsByTagName("MicrosoftPhoto:LastKeywordIPTC").Count <= 0)
                        {
                            var desc = xml_doc.CreateElement("rdf:Description", "rdf");
                            desc.SetAttribute("xmlns:MicrosoftPhoto", xmp_ns_lookup["MicrosoftPhoto"]);
                            desc.AppendChild(xml_doc.CreateElement("MicrosoftPhoto:LastKeywordIPTC", "MicrosoftPhoto"));
                            root_node.AppendChild(desc);
                        }
                        if (xml_doc.GetElementsByTagName("MicrosoftPhoto:LastKeywordIPTC_TIFF_IRB").Count <= 0)
                        {
                            var desc = xml_doc.CreateElement("rdf:Description", "rdf");
                            desc.SetAttribute("xmlns:MicrosoftPhoto", xmp_ns_lookup["MicrosoftPhoto"]);
                            desc.AppendChild(xml_doc.CreateElement("MicrosoftPhoto:LastKeywordIPTC_TIFF_IRB", "MicrosoftPhoto"));
                            root_node.AppendChild(desc);
                        }
                        #endregion
                        #region Copyright node
                        if (xml_doc.GetElementsByTagName("dc:rights").Count <= 0)
                        {
                            var desc = xml_doc.CreateElement("rdf:Description", "rdf");
                            desc.SetAttribute("xmlns:dc", xmp_ns_lookup["dc"]);
                            desc.AppendChild(xml_doc.CreateElement("dc:rights", "dc"));
                            root_node.AppendChild(desc);
                        }
                        #endregion
                        #region CreateTime node
                        if (xml_doc.GetElementsByTagName("xmp:CreateDate").Count <= 0)
                        {
                            var desc = xml_doc.CreateElement("rdf:Description", "rdf");
                            desc.SetAttribute("rdf:about", "uuid:faf5bdd5-ba3d-11da-ad31-d33d75182f1b");
                            desc.SetAttribute("xmlns:xmp", xmp_ns_lookup["xmp"]);
                            desc.AppendChild(xml_doc.CreateElement("xmp:CreateDate", "xmp"));
                            root_node.AppendChild(desc);
                        }
                        #endregion
                        #region ModifyDate node
                        if (xml_doc.GetElementsByTagName("xmp:ModifyDate").Count <= 0)
                        {
                            var desc = xml_doc.CreateElement("rdf:Description", "rdf");
                            desc.SetAttribute("rdf:about", "uuid:faf5bdd5-ba3d-11da-ad31-d33d75182f1b");
                            desc.SetAttribute("xmlns:xmp", xmp_ns_lookup["xmp"]);
                            desc.AppendChild(xml_doc.CreateElement("xmp:ModifyDate", "xmp"));
                            root_node.AppendChild(desc);
                        }
                        #endregion
                        #region DateTimeOriginal node
                        if (xml_doc.GetElementsByTagName("xmp:DateTimeOriginal").Count <= 0)
                        {
                            var desc = xml_doc.CreateElement("rdf:Description", "rdf");
                            desc.SetAttribute("rdf:about", "uuid:faf5bdd5-ba3d-11da-ad31-d33d75182f1b");
                            desc.SetAttribute("xmlns:xmp", xmp_ns_lookup["xmp"]);
                            desc.AppendChild(xml_doc.CreateElement("xmp:DateTimeOriginal", "xmp"));
                            root_node.AppendChild(desc);
                        }
                        #endregion
                        #region DateTimeDigitized node
                        if (xml_doc.GetElementsByTagName("xmp:DateTimeDigitized").Count <= 0)
                        {
                            var desc = xml_doc.CreateElement("rdf:Description", "rdf");
                            desc.SetAttribute("rdf:about", "uuid:faf5bdd5-ba3d-11da-ad31-d33d75182f1b");
                            desc.SetAttribute("xmlns:xmp", xmp_ns_lookup["xmp"]);
                            desc.AppendChild(xml_doc.CreateElement("xmp:DateTimeDigitized", "xmp"));
                            root_node.AppendChild(desc);
                        }
                        #endregion
                        #region Ranking/Rating node
                        if (xml_doc.GetElementsByTagName("xmp:Rating").Count <= 0 && rating > 0)
                        {
                            var desc = xml_doc.CreateElement("rdf:Description", "rdf");
                            desc.SetAttribute("rdf:about", "uuid:faf5bdd5-ba3d-11da-ad31-d33d75182f1b");
                            desc.SetAttribute("xmlns:xmp", xmp_ns_lookup["xmp"]);
                            desc.AppendChild(xml_doc.CreateElement("xmp:Rating", "xmp"));
                            root_node.AppendChild(desc);
                        }
                        if (xml_doc.GetElementsByTagName("MicrosoftPhoto:Rating").Count <= 0 && rating > 0)
                        {
                            var desc = xml_doc.CreateElement("rdf:Description", "rdf");
                            desc.SetAttribute("rdf:about", "uuid:faf5bdd5-ba3d-11da-ad31-d33d75182f1b");
                            desc.SetAttribute("xmlns:MicrosoftPhoto", xmp_ns_lookup["MicrosoftPhoto"]);
                            desc.AppendChild(xml_doc.CreateElement("MicrosoftPhoto:Rating", "MicrosoftPhoto"));
                            root_node.AppendChild(desc);
                        }
                        #endregion
                        #region EXIF DateTime node
                        if (xml_doc.GetElementsByTagName("exif:DateTimeDigitized").Count <= 0)
                        {
                            if (xml_doc.GetElementsByTagName("exif:DateTimeOriginal").Count > 0)
                            {
                                var node_msdt = xml_doc.GetElementsByTagName("exif:DateTimeOriginal").Item(0);
                                node_msdt.ParentNode.AppendChild(xml_doc.CreateElement("exif:DateTimeDigitized", "exif"));
                            }
                            else
                            {
                                var desc = xml_doc.CreateElement("rdf:Description", "rdf");
                                desc.SetAttribute("rdf:about", "");
                                desc.SetAttribute("xmlns:exif", xmp_ns_lookup["exif"]);
                                desc.AppendChild(xml_doc.CreateElement("exif:DateTimeDigitized", "exif"));
                                root_node.AppendChild(desc);
                            }
                        }
                        if (xml_doc.GetElementsByTagName("exif:DateTimeOriginal").Count <= 0)
                        {
                            if (xml_doc.GetElementsByTagName("exif:DateTimeDigitized").Count > 0)
                            {
                                var node_msdt = xml_doc.GetElementsByTagName("exif:DateTimeDigitized").Item(0);
                                node_msdt.ParentNode.AppendChild(xml_doc.CreateElement("exif:DateTimeOriginal", "exif"));
                            }
                            else
                            {
                                var desc = xml_doc.CreateElement("rdf:Description", "rdf");
                                desc.SetAttribute("rdf:about", "");
                                desc.SetAttribute("xmlns:exif", xmp_ns_lookup["exif"]);
                                desc.AppendChild(xml_doc.CreateElement("exif:DateTimeOriginal", "exif"));
                                root_node.AppendChild(desc);
                            }
                        }
                        #endregion
                        #region TIFF DateTime node
                        if (xml_doc.GetElementsByTagName("tiff:DateTime").Count <= 0)
                        {
                            var desc = xml_doc.CreateElement("rdf:Description", "rdf");
                            desc.SetAttribute("rdf:about", "");
                            desc.SetAttribute("xmlns:tiff", xmp_ns_lookup["tiff"]);
                            desc.AppendChild(xml_doc.CreateElement("tiff:DateTime", "tiff"));
                            root_node.AppendChild(desc);
                        }
                        #endregion
                        #region MicrosoftPhoto DateTime node
                        if (xml_doc.GetElementsByTagName("MicrosoftPhoto:DateAcquired").Count <= 0)
                        {
                            if (xml_doc.GetElementsByTagName("MicrosoftPhoto:DateTaken").Count > 0)
                            {
                                var node_msdt = xml_doc.GetElementsByTagName("MicrosoftPhoto:DateTaken").Item(0);
                                node_msdt.ParentNode.AppendChild(xml_doc.CreateElement("MicrosoftPhoto:DateAcquired", "MicrosoftPhoto"));
                            }
                            else
                            {
                                var desc = xml_doc.CreateElement("rdf:Description", "rdf");
                                desc.SetAttribute("rdf:about", "uuid:faf5bdd5-ba3d-11da-ad31-d33d75182f1b");
                                desc.SetAttribute("xmlns:MicrosoftPhoto", xmp_ns_lookup["MicrosoftPhoto"]);
                                desc.AppendChild(xml_doc.CreateElement("MicrosoftPhoto:DateAcquired", "MicrosoftPhoto"));
                                root_node.AppendChild(desc);
                            }
                        }
                        if (xml_doc.GetElementsByTagName("MicrosoftPhoto:DateTaken").Count <= 0)
                        {
                            if (xml_doc.GetElementsByTagName("MicrosoftPhoto:DateAcquired").Count > 0)
                            {
                                var node_msdt = xml_doc.GetElementsByTagName("MicrosoftPhoto:DateAcquired").Item(0);
                                node_msdt.ParentNode.AppendChild(xml_doc.CreateElement("MicrosoftPhoto:DateTaken", "MicrosoftPhoto"));
                            }
                            else
                            {
                                var desc = xml_doc.CreateElement("rdf:Description", "rdf");
                                desc.SetAttribute("rdf:about", "uuid:faf5bdd5-ba3d-11da-ad31-d33d75182f1b");
                                desc.SetAttribute("xmlns:MicrosoftPhoto", xmp_ns_lookup["MicrosoftPhoto"]);
                                desc.AppendChild(xml_doc.CreateElement("MicrosoftPhoto:DateTaken", "MicrosoftPhoto"));
                                root_node.AppendChild(desc);
                            }
                        }
                        #endregion

                        #region Remove duplicate node
                        var all_elements = new List<string>()
                        {
                            "dc:title",
                            "dc:description",
                            "dc:creator", "xmp:creator",
                            "dc:subject", "MicrosoftPhoto:LastKeywordXMP", "MicrosoftPhoto:LastKeywordIPTC", "MicrosoftPhoto:LastKeywordIPTC_TIFF_IRB",
                            "dc:rights",
                            "xmp:CreateDate", "xmp:ModifyDate", "xmp:DateTimeOriginal", "xmp:DateTimeDigitized", "xmp:MetadataDate",
                            "xmp:Rating", "MicrosoftPhoto:Rating",
                            "exif:DateTimeDigitized", "exif:DateTimeOriginal",
                            "tiff:DateTime",
                            "MicrosoftPhoto:DateAcquired", "MicrosoftPhoto:DateTaken",
                            "xmp:CreatorTool",
                        };
                        all_elements.AddRange(tag_author);
                        all_elements.AddRange(tag_comments);
                        all_elements.AddRange(tag_copyright);
                        all_elements.AddRange(tag_date);
                        all_elements.AddRange(tag_keywords);
                        all_elements.AddRange(tag_rating);
                        all_elements.AddRange(tag_subject);
                        all_elements.AddRange(tag_title);
                        foreach (var element in all_elements)
                        {
                            var nodes = xml_doc.GetElementsByTagName(element);
                            if (nodes.Count > 1)
                            {
                                for (var i = 1; i < nodes.Count; i++)
                                {
                                    nodes[i].ParentNode.RemoveChild(nodes[i]);
                                }
                            }
                        }
                        #endregion

                        #region xml nodes updating
                        var rdf_attr = "xmlns:rdf";
                        var rdf_value = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
                        Action<XmlElement, dynamic> add_rdf_li = new Action<XmlElement, dynamic>((element, text)=>
                        {
                            if (text is string && !string.IsNullOrEmpty(text as string))
                            {
                                var items = (text as string).Split(new string[] { ";", "#" }, StringSplitOptions.RemoveEmptyEntries).Select(k => k.Trim()).Where(k => !string.IsNullOrEmpty(k)).Distinct();
                                foreach (var item in items)
                                {
                                    var node_author_li = xml_doc.CreateElement("rdf:li", "rdf");
                                    node_author_li.InnerText = item;
                                    element.AppendChild(node_author_li);
                                }
                            }
                            else if(text is IEnumerable<string> && (text as IEnumerable<string>).Count() > 0)
                            {
                                foreach (var item in (text as IEnumerable<string>))
                                {
                                    var node_author_li = xml_doc.CreateElement("rdf:li", "rdf");
                                    node_author_li.InnerText = item;
                                    element.AppendChild(node_author_li);
                                }
                            }
                        });
                        foreach (XmlNode node in xml_doc.GetElementsByTagName("rdf:Description"))
                        {
                            var nodes = new List<XmlNode>();
                            foreach (XmlNode child in node.ChildNodes)
                            {
                                if (child.Name.Equals("dc:title", StringComparison.CurrentCultureIgnoreCase))
                                {
                                    child.RemoveAll();
                                    var node_title = xml_doc.CreateElement("rdf:Alt", "rdf");
                                    var node_title_li = xml_doc.CreateElement("rdf:li", "rdf");
                                    node_title_li.SetAttribute("xml:lang", "x-default");
                                    node_title_li.InnerText = title;
                                    node_title.AppendChild(node_title_li);
                                    child.AppendChild(node_title);
                                }
                                else if (child.Name.Equals("dc:description", StringComparison.CurrentCultureIgnoreCase))
                                {
                                    child.RemoveAll();
                                    var node_comment = xml_doc.CreateElement("rdf:Alt", "rdf");
                                    var node_comment_li = xml_doc.CreateElement("rdf:li", "rdf");
                                    node_comment_li.SetAttribute("xml:lang", "x-default");
                                    node_comment_li.InnerText = comment;
                                    node_comment.AppendChild(node_comment_li);
                                    child.AppendChild(node_comment);
                                }
                                else if (child.Name.Equals("xmp:creator", StringComparison.CurrentCultureIgnoreCase) ||
                                    child.Name.Equals("dc:creator", StringComparison.CurrentCultureIgnoreCase))
                                {
                                    child.RemoveAll();
                                    var node_author = xml_doc.CreateElement("rdf:Seq", "rdf");
                                    node_author.SetAttribute(rdf_attr, rdf_value);
                                    add_rdf_li.Invoke(node_author, authors);
                                    child.AppendChild(node_author);
                                }
                                else if (child.Name.Equals("dc:rights", StringComparison.CurrentCultureIgnoreCase))
                                {
                                    child.RemoveAll();
                                    var node_rights = xml_doc.CreateElement("rdf:Bag", "rdf");
                                    node_rights.SetAttribute(rdf_attr, rdf_value);
                                    add_rdf_li.Invoke(node_rights, copyright);
                                    child.AppendChild(node_rights);
                                }
                                else if (child.Name.Equals("dc:subject", StringComparison.CurrentCultureIgnoreCase) ||
                                    child.Name.StartsWith("MicrosoftPhoto:LastKeyword", StringComparison.CurrentCultureIgnoreCase))
                                {
                                    child.RemoveAll();
                                    var node_subject = xml_doc.CreateElement("rdf:Bag", "rdf");
                                    node_subject.SetAttribute(rdf_attr, rdf_value);
                                    add_rdf_li.Invoke(node_subject, keyword_list);
                                    child.AppendChild(node_subject);
                                }
                                else if (child.Name.Equals("MicrosoftPhoto:Rating", StringComparison.CurrentCultureIgnoreCase))
                                {
                                    child.InnerText = $"{rating}";
                                    //if (rating > 0) child.InnerText = $"{rating}";
                                    //else child.ParentNode.RemoveChild(child);
                                }
                                else if (child.Name.Equals("xmp:Rating", StringComparison.CurrentCultureIgnoreCase))
                                {
                                    var rating_level = 0;
                                    if (rating >= 99) rating_level = 5;
                                    else if (rating >= 75) rating_level = 4;
                                    else if (rating >= 50) rating_level = 3;
                                    else if (rating >= 25) rating_level = 2;
                                    else if (rating >= 01) rating_level = 1;
                                    child.InnerText = $"{rating_level}";
                                    //if (rating_level > 0) child.InnerText = $"{rating_level}";
                                    //else child.ParentNode.RemoveChild(child);
                                }
                                else if (child.Name.Equals("xmp:CreateDate", StringComparison.CurrentCultureIgnoreCase))
                                    child.InnerText = dc_xmp;
                                else if (child.Name.Equals("xmp:ModifyDate", StringComparison.CurrentCultureIgnoreCase))
                                    child.InnerText = dm_xmp;
                                else if (child.Name.Equals("xmp:DateTimeOriginal", StringComparison.CurrentCultureIgnoreCase))
                                    child.InnerText = dm_date;
                                else if (child.Name.Equals("xmp:DateTimeDigitized", StringComparison.CurrentCultureIgnoreCase))
                                    child.InnerText = dm_date;
                                else if (child.Name.Equals("MicrosoftPhoto:DateAcquired", StringComparison.CurrentCultureIgnoreCase))
                                    child.InnerText = dm_msxmp;
                                else if (child.Name.Equals("MicrosoftPhoto:DateTaken", StringComparison.CurrentCultureIgnoreCase))
                                    child.InnerText = dm_msxmp;
                                else if (child.Name.Equals("exif:DateTimeDigitized", StringComparison.CurrentCultureIgnoreCase))
                                    child.InnerText = dm_ms;
                                else if (child.Name.Equals("exif:DateTimeOriginal", StringComparison.CurrentCultureIgnoreCase))
                                    child.InnerText = dm_ms;
                                else if (child.Name.Equals("tiff:DateTime", StringComparison.CurrentCultureIgnoreCase))
                                    child.InnerText = dm_ms;

                                if (tag_date.Contains(node.Name, StringComparer.CurrentCultureIgnoreCase))
                                {
                                    if (nodes.Count(n => n.Name.Equals(child.Name, StringComparison.CurrentCultureIgnoreCase)) > 0) node.RemoveChild(child);
                                    else nodes.Add(child);
                                }
                            }
                            nodes.Clear();
                        }
                        #endregion
                        #region pretty xml
                        xml = FormatXML(xml_doc, true);
                        #endregion
                    }
                }
                catch
                {
                    #region Title
                    var pattern_title = @"(<dc:title>.*?<rdf:li.*?xml:lang='.*?')(>).*?(</rdf:li></rdf:Alt></dc:title>)";
                    if (Regex.IsMatch(xml, pattern_title, RegexOptions.IgnoreCase | RegexOptions.Multiline))
                    {
                        xml = Regex.Replace(xml, pattern_title, $"$1$2{title}$3", RegexOptions.IgnoreCase | RegexOptions.Multiline);
                        xml = xml.Replace("$2", ">");
                    }
                    else
                    {
                        var title_xml = $"<rdf:Description rdf:about='' xmlns:dc='http://purl.org/dc/elements/1.1/'><dc:title><rdf:Alt><rdf:li xml:lang='x-default'>{title}</rdf:li></rdf:Alt></dc:title></rdf:Description>";
                        xml = Regex.Replace(xml, @"(</rdf:RDF>.*?</x:xmpmeta>)", $"{title_xml}$1", RegexOptions.IgnoreCase);
                    }
                    #endregion
                    #region MS Photo DateAcquired
                    var pattern_ms_da = @"(<MicrosoftPhoto:DateAcquired>).*?(</MicrosoftPhoto:DateAcquired>)";
                    if (Regex.IsMatch(xml, pattern_ms_da, RegexOptions.IgnoreCase))
                    {
                        xml = Regex.Replace(xml, pattern_ms_da, $"$1{dm_msxmp}$2", RegexOptions.IgnoreCase);
                        xml = xml.Replace("$1", "<MicrosoftPhoto:DateAcquired>");
                    }
                    else
                    {
                        var msda_xml = $"<rdf:Description rdf:about=\"uuid:faf5bdd5-ba3d-11da-ad31-d33d75182f1b\" xmlns:MicrosoftPhoto=\"http://ns.microsoft.com/photo/1.0/\"><MicrosoftPhoto:DateAcquired>{dm_msxmp}</MicrosoftPhoto:DateAcquired></rdf:Description>";
                        xml = Regex.Replace(xml, @"(</rdf:RDF></x:xmpmeta>)", $"{msda_xml}$1", RegexOptions.IgnoreCase);
                    }
                    #endregion
                    #region MS Photo DateTaken
                    var pattern_ms_dt = @"(<MicrosoftPhoto:DateTaken>).*?(</MicrosoftPhoto:DateTaken>)";
                    if (Regex.IsMatch(xml, pattern_ms_dt, RegexOptions.IgnoreCase))
                    {
                        xml = Regex.Replace(xml, pattern_ms_dt, $"$1{dm_msxmp}$2", RegexOptions.IgnoreCase);
                        xml = xml.Replace("$1", "<MicrosoftPhoto:DateTaken>");
                    }
                    else
                    {
                        var msdt_xml = $"<rdf:Description rdf:about=\"uuid:faf5bdd5-ba3d-11da-ad31-d33d75182f1b\" xmlns:MicrosoftPhoto=\"http://ns.microsoft.com/photo/1.0/\"><MicrosoftPhoto:DateTaken>{dm_msxmp}</MicrosoftPhoto:DateTaken></rdf:Description>";
                        xml = Regex.Replace(xml, @"(</rdf:RDF></x:xmpmeta>)", $"{msdt_xml}$1", RegexOptions.IgnoreCase);
                    }
                    #endregion
                    #region tiff:DateTime
                    var pattern_tiff_dt = @"(<tiff:DateTime>).*?(</tiff:DateTime>)";
                    if (Regex.IsMatch(xml, pattern_tiff_dt, RegexOptions.IgnoreCase))
                    {
                        xml = Regex.Replace(xml, pattern_tiff_dt, $"$1{dm_ms}$2", RegexOptions.IgnoreCase);
                        xml = xml.Replace("$1", "<tiff:DateTime>");
                    }
                    else
                    {
                        var tiffdt_xml = $"<rdf:Description rdf:about='' xmlns:tiff='http://ns.adobe.com/tiff/1.0/'><tiff:DateTime>{dm_ms}</tiff:DateTime></rdf:Description>";
                        xml = Regex.Replace(xml, @"(</rdf:RDF></x:xmpmeta>)", $"{tiffdt_xml}$1", RegexOptions.IgnoreCase);
                    }
                    #endregion
                    #region exif:DateTimeDigitized
                    var pattern_exif_dd = @"(<exif:DateTimeDigitized>).*?(</exif:DateTimeDigitized>)";
                    if (Regex.IsMatch(xml, pattern_exif_dd, RegexOptions.IgnoreCase))
                    {
                        xml = Regex.Replace(xml, pattern_exif_dd, $"$1{dm_ms}$2", RegexOptions.IgnoreCase);
                        xml = xml.Replace("$1", "<exif:DateTimeDigitized>");
                    }
                    else
                    {
                        var exifdo_xml = $"<rdf:Description rdf:about='' xmlns:exif='http://ns.adobe.com/exif/1.0/'><exif:DateTimeDigitized>{dm_ms}</exif:DateTimeDigitized></rdf:Description>";
                        xml = Regex.Replace(xml, @"(</rdf:RDF></x:xmpmeta>)", $"{exifdo_xml}$1", RegexOptions.IgnoreCase);
                    }
                    #endregion
                    #region exif:DateTimeOriginal
                    var pattern_exif_do = @"(<exif:DateTimeOriginal>).*?(</exif:DateTimeOriginal>)";
                    if (Regex.IsMatch(xml, pattern_exif_do, RegexOptions.IgnoreCase))
                    {
                        xml = Regex.Replace(xml, pattern_exif_do, $"$1{dm_ms}$2", RegexOptions.IgnoreCase);
                        xml = xml.Replace("$1", "<exif:DateTimeOriginal>");
                    }
                    else
                    {
                        var exifdo_xml = $"<rdf:Description rdf:about='' xmlns:exif='http://ns.adobe.com/exif/1.0/'><exif:DateTimeOriginal>{dm_ms}</exif:DateTimeOriginal></rdf:Description>";
                        xml = Regex.Replace(xml, @"(</rdf:RDF></x:xmpmeta>)", $"{exifdo_xml}$1", RegexOptions.IgnoreCase);
                    }
                    #endregion
                }
            }
            return (xml);
        }
        #endregion

        #region below tags will be touching
        private static string[] tag_date = new string[] {
          "exif:DateTimeDigitized",
          "exif:DateTimeOriginal",
          "exif:DateTime",
          "MicrosoftPhoto:DateAcquired",
          "MicrosoftPhoto:DateTaken",
          //"png:tIME",
          "xmp:CreateDate",
          "xmp:ModifyDate",
          "xmp:DateTimeDigitized",
          "xmp:DateTimeOriginal",
          "Creation Time",
          "create-date",
          "modify-date",
          "tiff:DateTime",
          //"date:modify",
          //"date:create",
        };
        private static string[] tag_author = new string[] {
          "exif:Artist",
          "exif:WinXP-Author",
          "tiff:artist",
        };
        private static string[] tag_copyright = new string[] {
          "exif:Copyright",
          "tiff:copyright",
          //"iptc:CopyrightNotice",
        };
        private static string[] tag_title = new string[] {
          "exif:ImageDescription",
          "exif:WinXP-Title",
        };
        private static string[] tag_subject = new string[] {
          "exif:WinXP-Subject",
        };
        private static string[] tag_comments = new string[] {
          "exif:WinXP-Comments",
          "exif:UserComment"
        };
        private static string[] tag_keywords = new string[] {
          "exif:WinXP-Keywords",
          //"iptc:Keywords",
          "dc:Subject",
        };
        private static string[] tag_rating = new string[] {
          "Rating",
          "RatingPercent",
          "MicrosoftPhoto:Rating",
          "xmp:Rating",
        };
        #endregion

        #region Metadata Helper
        //private static MetaInfo CurrentMeta = null;
        private MetaInfo _current_meta_ = null;
        private MetaInfo CurrentMeta
        {
            get
            {
                if (_current_meta_ == null) _current_meta_ = new MetaInfo();
                Dispatcher.InvokeAsync(() =>
                {
                    _current_meta_.TouchProfiles = MetaInputTouchProfile.IsChecked ?? true;

                    _current_meta_.DateCreated = DateCreated.SelectedDate;
                    _current_meta_.DateModified = DateModified.SelectedDate;
                    _current_meta_.DateAccesed = DateAccessed.SelectedDate;

                    _current_meta_.DateAcquired = null;
                    _current_meta_.DateTaken = null;

                    _current_meta_.Title = string.IsNullOrEmpty(MetaInputTitleText.Text) ? null : MetaInputTitleText.Text;
                    _current_meta_.Subject = string.IsNullOrEmpty(MetaInputSubjectText.Text) ? null : MetaInputSubjectText.Text;
                    _current_meta_.Comment = string.IsNullOrEmpty(MetaInputCommentText.Text) ? null : MetaInputCommentText.Text;
                    _current_meta_.Keywords = string.IsNullOrEmpty(MetaInputKeywordsText.Text) ? null : string.Join("; ", MetaInputKeywordsText.Text.Split(LineBreak, StringSplitOptions.RemoveEmptyEntries).Distinct());
                    _current_meta_.Authors = string.IsNullOrEmpty(MetaInputAuthorText.Text) ? null : string.Join("; ", MetaInputAuthorText.Text.Split(LineBreak, StringSplitOptions.RemoveEmptyEntries).Distinct());
                    _current_meta_.Copyrights = string.IsNullOrEmpty(MetaInputCopyrightText.Text) ? null : string.Join("; ", MetaInputCopyrightText.Text.Split(LineBreak, StringSplitOptions.RemoveEmptyEntries).Distinct());
                    _current_meta_.RatingPercent = CurrentMetaRating;
                    _current_meta_.Rating = RatingToRanking(CurrentMetaRating);
                });
                return (_current_meta_);
            }
            set
            {
                _current_meta_ = value;
                Dispatcher.InvokeAsync(() =>
                {
                    if (_current_meta_ != null)
                    {
                        DateCreated.SelectedDate = _current_meta_.DateAcquired ?? _current_meta_.DateTaken ?? DateCreated.SelectedDate;
                        DateModified.SelectedDate = _current_meta_.DateAcquired ?? _current_meta_.DateTaken ?? DateModified.SelectedDate;
                        DateAccessed.SelectedDate = _current_meta_.DateAcquired ?? _current_meta_.DateTaken ?? DateAccessed.SelectedDate;

                        TimeCreated.Value = _current_meta_.DateAcquired ?? _current_meta_.DateTaken ?? TimeCreated.Value;
                        TimeModified.Value = _current_meta_.DateAcquired ?? _current_meta_.DateTaken ?? TimeModified.Value;
                        TimeAccessed.Value = _current_meta_.DateAcquired ?? _current_meta_.DateTaken ?? TimeAccessed.Value;

                        MetaInputTitleText.Text = _current_meta_.Title;
                        MetaInputSubjectText.Text = _current_meta_.Subject;
                        MetaInputCommentText.Text = _current_meta_.Comment;
                        MetaInputKeywordsText.Text = _current_meta_.Keywords;
                        MetaInputAuthorText.Text = _current_meta_.Authors;
                        MetaInputCopyrightText.Text = _current_meta_.Copyrights;
                        CurrentMetaRating = _current_meta_.RatingPercent ?? RankingToRating(_current_meta_.Rating);
                    }
                });
            }
        }

        public static bool IsValidRead(MagickImage image)
        {
            return (image is MagickImage && image.FormatInfo.IsReadable);
        }

        public static bool IsValidWrite(MagickImage image)
        {
            return (image is MagickImage && image.FormatInfo.IsWritable);
        }

        public static Point GetSystemDPI()
        {
            var result = new Point(96, 96);
            try
            {
                BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
                var dpiXProperty = typeof(SystemParameters).GetProperty("DpiX", flags);
                //var dpiYProperty = typeof(SystemParameters).GetProperty("DpiY", flags);
                var dpiYProperty = typeof(SystemParameters).GetProperty("Dpi", flags);
                if (dpiXProperty != null) { result.X = (int)dpiXProperty.GetValue(null, null); }
                if (dpiYProperty != null) { result.Y = (int)dpiYProperty.GetValue(null, null); }
            }
            catch (Exception ex) { Log(ex.Message); }
            return (result);
        }

        public static void FixDPI(MagickImage image)
        {
            if (IsValidRead(image))
            {
                var dpi = GetSystemDPI();
                if (image.Density is Density && image.Density.X > 0 && image.Density.Y > 0)
                {
                    var unit = image.Density.ChangeUnits(DensityUnit.PixelsPerInch);
                    if (unit.X <= 0 || unit.Y <= 0)
                        image.Density = new Density(dpi.X, dpi.Y, DensityUnit.PixelsPerInch);
                    else
                        image.Density = new Density(Math.Round(unit.X), Math.Round(unit.Y), DensityUnit.PixelsPerInch);
                }
                else image.Density = new Density(dpi.X, dpi.Y, DensityUnit.PixelsPerInch);
            }
        }

        public static DateTime? ParseDateTime(string text, bool is_file = true)
        {
            DateTime? result = null;

            try
            {
                var file = text;
                if (is_file) text = Path.GetFileNameWithoutExtension(text);
                var trim_chars = new char[] { '_', '.', ' ' };
                DateTime dt;
                //‎2022‎年‎02‎月‎04‎日，‏‎16:49:26
                var pattens = new string[]
                {
                    @"‎(\d{2,4}.*?年.*?\d{1,2}.*?‎月.*?\d{1,2}.*?‎日.*?[，,T].*?\d{1,2}:\d{1,2}:\d{1,2})",
                    @"(\d{2,4})[ :_\-/\.\\年]{0,3}(\d{1,2})[ :_\-/\.\\月]{0,3}(\d{1,2})[ :_\-/\.\\日]{0,3}[ ,:_\-/\.\\T]?(\d{1,2})[ :_\-\.时]{0,3}(\d{1,2})[ :_\-\.分]{0,3}(\d{1,2})[ :_\-\.秒]{0,3}",
                    @"(\d{2,4})[ :_\-/\.\\年]{0,3}((\d{1,2})[ :_\-/\.\\月日]{0,3}){2}[ ,:_\-/\.\\T]?(\d{2})[:_\-/\.\\时分秒]{0,3}",
                    @"(\d{4}[ :_\-/\.\\年]{0,3})(\d{2}[ :_\-/\.\\月日时分秒T]{0,3})+",
                    @"(\d{2}[ :_\-/\.\\月日]{0,3})+(\d{4})[ \-,:T](\d{2}[:_\-\.\\时分秒]{0,3}){3}",
                    @"(\d{2}[ :_\-/\.\\月日时分秒]{0,3})+(\d{4})",
                    @"(\d{4})(\d{2})(\d{2})-(\d{2})(\d{2})(\d{2})",
                };

                text = Regex.Replace(text, @"[\u0000-\u001F\u007F\u2000-\u201F\u207F]", "");
                text = Regex.Replace(text, @"[，]", ",");
                text = Regex.Replace(text, @"^(\d{8,}_\d{4,}_)", "", RegexOptions.IgnoreCase);
                if (DateTime.TryParse(text, out dt)) result = dt;
                else
                {
                    foreach (var patten in pattens)
                    {
                        if (Regex.IsMatch(text, patten))
                        {
                            var match = Regex.Replace(text.Replace("_", " "), $@"^.*?({patten}).*?$", "$1");
                            //match = Regex.Replace(match.Trim(trim_chars), patten, (m) => { return ($" {m.Value.Trim(trim_chars)} "); });
                            match = Regex.Replace(match.Trim(trim_chars), patten, "$1/$2/$3 $4:$5:$6");
                            if (DateTime.TryParse(match, out dt)) { result = dt; Log($"{file} => {dt.ToString("yyyy/MM/dd HH:mm:ss")}"); break; }
                        }
                    }
                }
            }
            catch (Exception ex) { Log(ex.Message); }

            return (result);
        }

        public static string GetAttribute(MagickImage image, string attr)
        {
            string result = null;
            try
            {
                if (image is MagickImage && image.FormatInfo.IsReadable)
                {
                    var is_msb = image.Endian == Endian.MSB;

                    var exif = image.HasProfile("exif") ? image.GetExifProfile() : new ExifProfile();
                    var iptc = image.HasProfile("iptc") ? image.GetIptcProfile() : new IptcProfile();
                    Type exiftag_type = typeof(ImageMagick.ExifTag);

                    result = attr.Contains("WinXP") ? BytesToUnicode(image.GetAttribute(attr)) : image.GetAttribute(attr);
                    if (attr.StartsWith("exif:") && !attr.Contains("WinXP"))
                    {
                        var tag_name =  attr.Contains("WinXP") ? $"XP{attr.Substring(11)}" : attr.Substring(5);
                        if (tag_name.Equals("FlashPixVersion")) tag_name = "FlashpixVersion";
                        dynamic tag_property = exiftag_type.GetProperty(tag_name) ?? exiftag_type.GetProperty($"{tag_name}s") ?? exiftag_type.GetProperty(tag_name.Substring(0, tag_name.Length-1));
                        if (tag_property != null)
                        {
                            IExifValue tag_value = exif.GetValue(tag_property.GetValue(exif));
                            if (tag_value != null)
                            {
                                if (tag_value.DataType == ExifDataType.String)
                                    result = tag_value.GetValue() as string;
                                else if (tag_value.DataType == ExifDataType.Rational && tag_value.IsArray)
                                {
                                    var rs = (Rational[])(tag_value.GetValue());
                                    var rr = new List<string>();
                                    foreach (var r in rs)
                                    {
                                        var ri = r.Numerator / r.Denominator;
                                        var rf = r.ToDouble();
                                        rr.Add(ri == rf ? $"{ri}" : (rf > 0 ? $"{rf:F1}" : $"{r.Numerator}/{r.Denominator}"));
                                    }
                                    result = string.Join(", ", rr);
                                }
                                else if (tag_value.DataType == ExifDataType.Rational)
                                {
                                    var r = (Rational)(tag_value.GetValue());
                                    var ri = r.Numerator / r.Denominator;
                                    var rf = r.ToDouble();
                                    result = ri == rf ? $"{ri}" : (rf > 0 ? $"{rf:F1}" : $"{r.Numerator}/{r.Denominator}");
                                }
                                else if (tag_value.DataType == ExifDataType.Rational && tag_value.IsArray)
                                {
                                    var rs = (SignedRational[])(tag_value.GetValue());
                                    var rr = new List<string>();
                                    foreach (var r in rs)
                                    {
                                        var ri = r.Numerator / r.Denominator;
                                        var rf = r.ToDouble();
                                        rr.Add(ri == rf ? $"{ri}" : (rf > 0 ? $"{rf:F1}" : $"{r.Numerator}/{r.Denominator}"));
                                    }
                                    result = string.Join(", ", rr);
                                }
                                else if (tag_value.DataType == ExifDataType.SignedRational)
                                {
                                    var r = (SignedRational)(tag_value.GetValue());
                                    var ri = r.Numerator / r.Denominator;
                                    var rf = r.ToDouble();
                                    result = ri == rf ? $"{ri}" : (rf > 0 ? $"{rf:F0}" : $"{r.Numerator}/{r.Denominator}");
                                }
                                else if (tag_value.DataType == ExifDataType.Undefined && tag_value.IsArray)
                                {
                                    if (tag_value.Tag == ImageMagick.ExifTag.ExifVersion)
                                        result = BytesToString(tag_value.GetValue() as byte[], true, is_msb);
                                    else if (tag_value.Tag == ImageMagick.ExifTag.GPSProcessingMethod || tag_value.Tag == ImageMagick.ExifTag.MakerNote)
                                        result = Encoding.UTF8.GetString(tag_value.GetValue() as byte[]).TrimEnd('\0').Trim();
                                    else
                                        result = BytesToString(tag_value.GetValue() as byte[], false, is_msb);
                                }
                                else if (tag_value.DataType == ExifDataType.Byte && tag_value.IsArray)
                                {
                                    result = BytesToString(tag_value.GetValue() as byte[], msb: is_msb);
                                }
                                else if (tag_value.DataType == ExifDataType.Unknown && tag_value.IsArray)
                                {
                                    var is_ascii = tag_value.Tag.ToString().Contains("Version");
                                    result = BytesToString(tag_value.GetValue() as byte[], is_ascii, is_msb);
                                }
                            }
                        }
                        else if (attr.Equals("exif:ExtensibleMetadataPlatform"))
                        {
                            var xmp_tag = exif.Values.Where(t => t.Tag == ImageMagick.ExifTag.XMP);
                            if (xmp_tag.Count() > 0)
                            {
                                var bytes = xmp_tag.First().GetValue() as byte[];
                                result = Encoding.UTF8.GetString(bytes);
                            }
                        }
                    }
                    else if (attr.StartsWith("iptc:"))
                    {
                        Type tag_type = typeof(IptcTag);
                        var tag_name = attr.Substring(5);
                        dynamic tag_property = tag_type.GetProperty(tag_name);
                        if (tag_property != null)
                        {
                            IEnumerable<IIptcValue> iptc_values = iptc.GetAllValues(tag_property);
                            var values = new List<string>();
                            foreach (var tag_value in iptc_values)
                            {
                                if (tag_value != null) values.Add(tag_value.Value as string);
                            }
                            result = string.Join("; ", values);
                        }
                    }

                    if (attr.StartsWith("date:"))
                    {
                        DateTime dt;
                        if (DateTime.TryParse(result, out dt)) result = dt.ToString("yyyy-MM-ddTHH:mm:sszzz");
                    }

                    if (!string.IsNullOrEmpty(result)) result = result.Replace("\0", string.Empty).TrimEnd('\0');
                    if (!string.IsNullOrEmpty(result) && Regex.IsMatch($"{result.TrimEnd().TrimEnd(',')},", @"((\d{1,3}) ?, ?){16,}", RegexOptions.IgnoreCase)) result = ByteStringToString(result);
                }
            }
            catch (Exception ex) { Log(ex.Message); }
            return (result);
        }

        public static void SetAttribute(MagickImage image, string attr, dynamic value)
        {
            try
            {
                if (image is MagickImage && image.FormatInfo.IsReadable && value != null)
                {
                    var exif = image.HasProfile("exif") ? image.GetExifProfile() : new ExifProfile();
                    var iptc = image.HasProfile("iptc") ? image.GetIptcProfile() : new IptcProfile();

                    var value_old = GetAttribute(image, attr);
                    image.SetAttribute(attr, value is bool ? value : (attr.Contains("WinXP") ? UnicodeToBytes(value) : value.ToString()));
                    if (attr.StartsWith("exif:"))
                    {
                        Type exiftag_type = typeof(ImageMagick.ExifTag);
                        var IsWinXP = attr.Contains("WinXP");// || attr.StartsWith("Xp");
                        var tag_name =  IsWinXP ? $"XP{attr.Substring(11)}" : attr.Substring(5);
                        dynamic tag_property = exiftag_type.GetProperty(tag_name) ?? exiftag_type.GetProperty($"{tag_name}s") ?? exiftag_type.GetProperty(tag_name.Substring(0, tag_name.Length-1));
                        if (tag_property != null)
                        {
                            var tag_type = (tag_property as PropertyInfo).GetMethod.ReturnType.GenericTypeArguments.First();
                            if (tag_type == typeof(byte))
                            {
                                byte v;
                                if (byte.TryParse(value, out v)) exif.SetValue(tag_property.GetValue(exif), v);
                            }
                            else if (tag_type == typeof(byte[]) && value is string)
                            {
                                byte[] v = !IsWinXP && image.Endian == Endian.MSB ? Encoding.BigEndianUnicode.GetBytes(value) : Encoding.Unicode.GetBytes(value);
                                if (tag_name.Equals("UserComment")) v = Encoding.ASCII.GetBytes("UNICODE\0").Concat(v).ToArray();
                                exif.SetValue(tag_property.GetValue(exif), v);
                            }
                            else if (tag_type == typeof(ushort) || tag_type == typeof(ushort))
                            {
                                ushort v;
                                if (ushort.TryParse(value, out v)) exif.SetValue(tag_property.GetValue(exif), v);
                            }
                            else if (tag_type == typeof(ushort[]) && value is string && !string.IsNullOrEmpty(value))
                            {
                                ushort[] v;
                                var vl = (value as string).Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                                v = vl.Select(u => ushort.Parse(u)).ToArray();
                                exif.SetValue(tag_property.GetValue(exif), v);
                            }
                            else if (tag_type == typeof(Number))
                            {
                                uint v;
                                if (uint.TryParse(value, out v)) exif.SetValue(tag_property.GetValue(exif), new Number(v));
                            }
                            else
                                exif.SetValue(tag_property.GetValue(exif), value);
                        }
                        //if (!image.HasProfile("exif"))
                        if (exif is ExifProfile && exif.Values.Count() > 0)
                            image.SetProfile(exif);
                    }
                    else if (attr.StartsWith("iptc:"))
                    {
                        Type tag_type = typeof(IptcTag);
                        var tag_name = attr.Substring(5);
                        dynamic tag_property = tag_type.GetProperty(tag_name);
                        if (tag_property != null)
                        {
                            iptc.SetValue(tag_property, value);
                        }
                        //if (!image.HasProfile("iptc"))
                        if (iptc is IptcProfile && iptc.Values.Count() > 0)
                            image.SetProfile(iptc);
                    }
                }
            }
            catch (Exception ex) { Log(ex.Message); }
        }

        #region Add/Remove/Replace/Empty exif properties
        public static MetaInfo ChangeTitle(MetaInfo meta, string title, ChangePropertyMode mode = ChangePropertyMode.None)
        {
            if (meta is MetaInfo && mode != ChangePropertyMode.None)
            {
                if (mode == ChangePropertyMode.Append)
                {
                    meta.Title = $"{meta.Title}; {title}";
                }
                else if (mode == ChangePropertyMode.Remove)
                {
                    meta.Title = meta.Title.Replace(title, "");
                }
                else if (mode == ChangePropertyMode.Replace)
                {
                    meta.Title = title;
                }
                else if (mode == ChangePropertyMode.Empty)
                {
                    meta.Title = string.Empty;
                }
            }
            return (meta);
        }

        public static void ChangeTitle(string file, string title, ChangePropertyMode mode = ChangePropertyMode.None)
        {
            if (File.Exists(file) && !string.IsNullOrEmpty(title))
            {
                try
                {
                    var meta = GetMetaInfo(file);
                    meta.TouchProfiles = false;
                    meta.ChangeProperties = ChangePropertyType.Title;
                    meta = ChangeTitle(meta, title, mode);
                    TouchMeta(file, force: true, meta: meta);
                }
                catch (Exception ex) { Log(ex.Message); }
            }
        }

        public static MetaInfo ChangeSubject(MetaInfo meta, string subject, ChangePropertyMode mode = ChangePropertyMode.None)
        {
            if (meta is MetaInfo && mode != ChangePropertyMode.None)
            {
                if (mode == ChangePropertyMode.Append)
                {
                    meta.Subject = $"{meta.Subject}; {subject}";
                }
                else if (mode == ChangePropertyMode.Remove)
                {
                    meta.Subject = meta.Subject.Replace(subject, "");
                }
                else if (mode == ChangePropertyMode.Replace)
                {
                    meta.Subject = subject;
                }
                else if (mode == ChangePropertyMode.Empty)
                {
                    meta.Subject = string.Empty;
                }
            }
            return (meta);
        }

        public static void ChangeSubject(string file, string subject, ChangePropertyMode mode = ChangePropertyMode.None)
        {
            if (File.Exists(file) && !string.IsNullOrEmpty(subject))
            {
                try
                {
                    var meta = GetMetaInfo(file);
                    meta.TouchProfiles = false;
                    meta.ChangeProperties = ChangePropertyType.Subject;
                    meta = ChangeSubject(meta, subject, mode);
                    TouchMeta(file, force: true, meta: meta);
                }
                catch (Exception ex) { Log(ex.Message); }
            }
        }

        public static MetaInfo ChangeKeywords(MetaInfo meta, string[] keywords, ChangePropertyMode mode = ChangePropertyMode.None)
        {
            if (meta is MetaInfo && mode != ChangePropertyMode.None)
            {
                var tags_old = string.IsNullOrEmpty(meta.Keywords) ? new string[] { } :  meta.Keywords.Split(';');
                if (mode == ChangePropertyMode.Append)
                {
                    meta.Keywords = string.Join("; ", tags_old.Select(t => t.Trim()).Union(keywords.Select(t => t.Trim())));
                }
                else if (mode == ChangePropertyMode.Remove)
                {
                    //meta.Keywords = string.Join("; ", tags_old.Select(t => t.Trim()).Where(t => !(tags.Select(k => k.Trim())).Contains(t)));
                    meta.Keywords = string.Join("; ", tags_old.Select(t => t.Trim()).Except(keywords.Select(t => t.Trim())));
                }
                else if (mode == ChangePropertyMode.Replace)
                {
                    meta.Keywords = string.Join("; ", keywords.Select(t => t.Trim()));
                }
                else if (mode == ChangePropertyMode.Empty)
                {
                    meta.Keywords = string.Empty;
                }
                if (!string.IsNullOrEmpty(meta.Keywords) && meta.Keywords.EndsWith(";")) meta.Keywords += ";";
            }
            return (meta);
        }

        public static void ChangeKeywords(string file, string[] keywords, ChangePropertyMode mode = ChangePropertyMode.None)
        {
            if (File.Exists(file) && keywords is string[] && keywords.Length > 0)
            {
                try
                {
                    var meta = GetMetaInfo(file);
                    meta.TouchProfiles = false;
                    meta.ChangeProperties = ChangePropertyType.Keywords;
                    meta = ChangeKeywords(meta, keywords, mode);
                    TouchMeta(file, force: true, meta: meta);
                }
                catch (Exception ex) { Log(ex.Message); }
            }
        }

        public static MetaInfo ChangeComment(MetaInfo meta, string comment, ChangePropertyMode mode = ChangePropertyMode.None)
        {
            if (meta is MetaInfo && mode != ChangePropertyMode.None)
            {
                if (mode == ChangePropertyMode.Append)
                {
                    meta.Comment = $"{meta.Comment}; {comment}";
                }
                else if (mode == ChangePropertyMode.Remove)
                {
                    meta.Comment = meta.Comment.Replace(comment, "");
                }
                else if (mode == ChangePropertyMode.Replace)
                {
                    meta.Comment = comment;
                }
                else if (mode == ChangePropertyMode.Empty)
                {
                    meta.Comment = string.Empty;
                }
            }
            return (meta);
        }

        public static void ChangeComment(string file, string comment, ChangePropertyMode mode = ChangePropertyMode.None)
        {
            if (File.Exists(file) && !string.IsNullOrEmpty(comment))
            {
                try
                {
                    var meta = GetMetaInfo(file);
                    meta.TouchProfiles = false;
                    meta.ChangeProperties = ChangePropertyType.Comment;
                    meta = ChangeSubject(meta, comment, mode);
                    TouchMeta(file, force: true, meta: meta);
                }
                catch (Exception ex) { Log(ex.Message); }
            }
        }

        public static MetaInfo ChangeAuthors(MetaInfo meta, string[] authors, ChangePropertyMode mode = ChangePropertyMode.None)
        {
            if (meta is MetaInfo && mode != ChangePropertyMode.None)
            {
                var authors_old = string.IsNullOrEmpty(meta.Authors) ? new string[] { } : meta.Authors.Split(';');
                if (mode == ChangePropertyMode.Append)
                {
                    meta.Authors = string.Join("; ", authors_old.Select(t => t.Trim()).Union(authors.Select(t => t.Trim())));
                }
                else if (mode == ChangePropertyMode.Remove)
                {
                    //meta.Author = string.Join("; ", authors_old.Select(t => t.Trim()).Where(t => !(authors.Select(k => k.Trim())).Contains(t)));
                    meta.Authors = string.Join("; ", authors_old.Select(t => t.Trim()).Except(authors.Select(t => t.Trim())));
                }
                else if (mode == ChangePropertyMode.Replace)
                {
                    meta.Authors = string.Join("; ", authors.Select(t => t.Trim()));
                }
                else if (mode == ChangePropertyMode.Empty)
                {
                    meta.Authors = string.Empty;
                }
                if (!string.IsNullOrEmpty(meta.Authors) && meta.Authors.EndsWith(";")) meta.Authors += ";";
            }
            return (meta);
        }

        public static void ChangeAuthors(string file, string[] authors, ChangePropertyMode mode = ChangePropertyMode.None)
        {
            if (File.Exists(file) && authors is string[] && authors.Length > 0)
            {
                try
                {
                    var meta = GetMetaInfo(file);
                    meta.TouchProfiles = false;
                    meta.ChangeProperties = ChangePropertyType.Authors;
                    meta = ChangeAuthors(meta, authors, mode);
                    TouchMeta(file, force: true, meta: meta);
                }
                catch (Exception ex) { Log(ex.Message); }
            }
        }

        public static MetaInfo ChangeCopyrights(MetaInfo meta, string[] copyrights, ChangePropertyMode mode = ChangePropertyMode.None)
        {
            if (meta is MetaInfo && mode != ChangePropertyMode.None)
            {
                var copyrights_old = string.IsNullOrEmpty(meta.Copyrights) ? new string[] { } : meta.Copyrights.Split(';');
                if (mode == ChangePropertyMode.Append)
                {
                    meta.Copyrights = string.Join("; ", copyrights_old.Select(t => t.Trim()).Union(copyrights.Select(t => t.Trim())));
                }
                else if (mode == ChangePropertyMode.Remove)
                {
                    //meta.Copyright = string.Join("; ", copyrights_old.Select(t => t.Trim()).Where(t => !(copyrights.Select(k => k.Trim())).Contains(t)));
                    meta.Copyrights = string.Join("; ", copyrights_old.Select(t => t.Trim()).Except(copyrights.Select(t => t.Trim())));
                }
                else if (mode == ChangePropertyMode.Replace)
                {
                    meta.Copyrights = string.Join("; ", copyrights.Select(t => t.Trim()));
                }
                else if (mode == ChangePropertyMode.Empty)
                {
                    meta.Copyrights = string.Empty;
                }
                if (!string.IsNullOrEmpty(meta.Copyrights) && meta.Copyrights.EndsWith(";")) meta.Copyrights += ";";
            }
            return (meta);
        }

        public static void ChangeCopyrights(string file, string[] copyrights, ChangePropertyMode mode = ChangePropertyMode.None)
        {
            if (File.Exists(file) && copyrights is string[] && copyrights.Length > 0)
            {
                try
                {
                    var meta = GetMetaInfo(file);
                    meta.TouchProfiles = false;
                    meta.ChangeProperties = ChangePropertyType.Copyrights;
                    meta = ChangeCopyrights(meta, copyrights, mode);
                    TouchMeta(file, force: true, meta: meta);
                }
                catch (Exception ex) { Log(ex.Message); }
            }
        }

        public static MetaInfo ChangeRanking(MetaInfo meta, int ranking, ChangePropertyMode mode = ChangePropertyMode.None)
        {
            if (meta is MetaInfo && mode != ChangePropertyMode.None)
            {
                if (mode == ChangePropertyMode.Append)
                {
                    meta.RatingPercent = Math.Max(0, Math.Min(meta.RatingPercent ?? 0 + ranking, 5));
                }
                else if (mode == ChangePropertyMode.Remove)
                {
                    meta.RatingPercent = Math.Min(5, Math.Max(meta.RatingPercent ?? 0 - ranking, 0));
                }
                else if (mode == ChangePropertyMode.Replace)
                {
                    meta.RatingPercent = Math.Max(0, Math.Min(5, ranking));
                }
                else if (mode == ChangePropertyMode.Empty)
                {
                    meta.RatingPercent = 0;
                }
                meta.Rating = RatingToRanking(meta.RatingPercent);
            }
            return (meta);
        }

        public static void ChangeRanking(string file, int ranking, ChangePropertyMode mode = ChangePropertyMode.None)
        {
            if (File.Exists(file))
            {
                try
                {
                    var meta = GetMetaInfo(file);
                    meta.TouchProfiles = false;
                    meta.ChangeProperties = ChangePropertyType.Ranking;
                    meta = ChangeRanking(meta, ranking, mode);
                    TouchMeta(file, force: true, meta: meta);
                }
                catch (Exception ex) { Log(ex.Message); }
            }
        }

        public static MetaInfo ChangeRating(MetaInfo meta, int rating, ChangePropertyMode mode = ChangePropertyMode.None)
        {
            if (meta is MetaInfo && mode != ChangePropertyMode.None)
            {
                if (mode == ChangePropertyMode.Append)
                {
                    meta.RatingPercent = Math.Max(0, Math.Min(100, meta.RatingPercent ?? 0 + rating));
                }
                else if (mode == ChangePropertyMode.Remove)
                {
                    meta.RatingPercent = Math.Max(0, Math.Min(100, meta.RatingPercent ?? 0 - rating));
                }
                else if (mode == ChangePropertyMode.Replace)
                {
                    meta.RatingPercent = Math.Max(0, Math.Min(100, rating));
                }
                else if (mode == ChangePropertyMode.Empty)
                {
                    meta.RatingPercent = 0;
                }
                meta.Rating = RatingToRanking(meta.RatingPercent);
            }
            return (meta);
        }

        public static void ChangeRating(string file, int rating, ChangePropertyMode mode = ChangePropertyMode.None)
        {
            if (File.Exists(file))
            {
                try
                {
                    var meta = GetMetaInfo(file);
                    meta.TouchProfiles = false;
                    meta.ChangeProperties = ChangePropertyType.Rating;
                    meta = ChangeRating(meta, rating, mode);
                    TouchMeta(file, force: true, meta: meta);
                }
                catch (Exception ex) { Log(ex.Message); }
            }
        }

        public static void ChangeProperties(string file, MetaInfo meta_new, ChangePropertyType type = ChangePropertyType.None, ChangePropertyMode mode = ChangePropertyMode.None)
        {
            if (File.Exists(file))
            {
                try
                {
                    var meta = GetMetaInfo(file);
                    meta.TouchProfiles = false;

                    if (type == ChangePropertyType.Smart)
                    {
                        meta.ChangeProperties = ChangePropertyType.None;
                        if (!string.IsNullOrEmpty(meta_new.Title) || mode == ChangePropertyMode.Empty)
                            meta.ChangeProperties |= ChangePropertyType.Title;
                        if (!string.IsNullOrEmpty(meta_new.Subject) || mode == ChangePropertyMode.Empty)
                            meta.ChangeProperties |= ChangePropertyType.Subject;
                        if (!string.IsNullOrEmpty(meta_new.Keywords) || mode == ChangePropertyMode.Empty)
                            meta.ChangeProperties |= ChangePropertyType.Keywords;
                        if (!string.IsNullOrEmpty(meta_new.Comment) || mode == ChangePropertyMode.Empty)
                            meta.ChangeProperties |= ChangePropertyType.Comment;
                        if (!string.IsNullOrEmpty(meta_new.Authors) || mode == ChangePropertyMode.Empty)
                            meta.ChangeProperties |= ChangePropertyType.Authors;
                        if (!string.IsNullOrEmpty(meta_new.Copyrights) || mode == ChangePropertyMode.Empty)
                            meta.ChangeProperties |= ChangePropertyType.Copyrights;
                    }
                    else meta.ChangeProperties = type;

                    if (type.HasFlag(ChangePropertyType.Title))
                        meta = ChangeTitle(meta, meta_new.Title, mode);
                    if (type.HasFlag(ChangePropertyType.Subject))
                        meta = ChangeSubject(meta, meta_new.Subject, mode);
                    if (type.HasFlag(ChangePropertyType.Keywords))
                        meta = ChangeKeywords(meta, meta_new.Keywords.Split(';'), mode);
                    if (type.HasFlag(ChangePropertyType.Comment))
                        meta = ChangeComment(meta, meta_new.Comment, mode);
                    if (type.HasFlag(ChangePropertyType.Authors))
                        meta = ChangeAuthors(meta, meta_new.Authors.Split(';'), mode);
                    if (type.HasFlag(ChangePropertyType.Copyrights))
                        meta = ChangeCopyrights(meta, meta_new.Copyrights.Split(';'), mode);
                    if (type.HasFlag(ChangePropertyType.Rating))
                        meta = ChangeRating(meta, meta_new.RatingPercent ?? 0, mode);
                    if (type.HasFlag(ChangePropertyType.Ranking))
                        meta = ChangeRanking(meta, meta_new.Rating ?? 0, mode);

                    TouchMeta(file, force: true, meta: meta);
                }
                catch (Exception ex) { Log(ex.Message); }
            }
        }
        #endregion

        #region PngCs Routines for Update PNG Image Metadata
        private static string[] png_meta_chunk_text = new string[]{ "iTXt", "tEXt", "zTXt" };
        //private static int GZIP_MAGIC = 35615;
        private static byte[] GZIP_MAGIC_HEADER = new byte[] { 0x1F, 0x8B, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
        private static string GzipBytesToText(byte[] bytes, Encoding encoding = default(Encoding), int skip = 2)
        {
            var result = string.Empty;
            try
            {
                if (encoding == default(Encoding)) encoding = Encoding.UTF8;
                ///
                /// below line is need in VS2015 (C# 6.0) must add a gzip header struct & skip two bytes of zlib header, 
                /// VX2017 (c# 7.0+) work fina willout this line
                ///
#if DEBUG
                if (bytes[0] != GZIP_MAGIC_HEADER[0] && bytes[1] != GZIP_MAGIC_HEADER[1]) bytes = GZIP_MAGIC_HEADER.Concat(bytes.Skip(2)).ToArray();
#endif
                using (var msi = new MemoryStream(bytes))
                {
                    using (var mso = new MemoryStream())
                    {
                        using (var ds = new System.IO.Compression.GZipStream(msi, System.IO.Compression.CompressionMode.Decompress))
                        {
                            ds.CopyTo(mso);
                            ds.Close();
                        }
                        var ret = mso.ToArray();
                        try
                        {
                            var text = string.Join("", encoding.GetString(ret).Split().Skip(2));
                            var buff = new byte[text.Length/2];
                            for (var i = 0; i < text.Length / 2; i++)
                            {
                                buff[i] = Convert.ToByte($"0x{text[2 * i]}{text[2 * i + 1]}", 16);
                            }
                            result = encoding.GetString(buff.Skip(skip).ToArray());
                        }
                        catch (Exception ex) { Log($"{ex.Message}{Environment.NewLine}{ex.StackTrace}"); };
                    }
                }
            }
            catch (Exception ex) { Log($"{ex.Message}{Environment.NewLine}{ex.StackTrace}"); }
            return (result);
        }

        private static Dictionary<string, string> GetPngMetaInfo(Stream src, Encoding encoding = default(Encoding), bool full_field = true)
        {
            var result = new Dictionary<string, string>();
            try
            {
                if (src is Stream && src.CanRead && src.Length > 0)
                {
                    if (encoding == default(Encoding)) encoding = Encoding.UTF8;
                    var png_r  = new Hjg.Pngcs.PngReader(src);
                    if (png_r is Hjg.Pngcs.PngReader)
                    {
                        png_r.ChunkLoadBehaviour = Hjg.Pngcs.Chunks.ChunkLoadBehaviour.LOAD_CHUNK_ALWAYS;
                        var png_chunks = png_r.GetChunksList();
                        foreach (var chunk in png_chunks.GetChunks())
                        {
                            if (png_meta_chunk_text.Contains(chunk.Id))
                            {
                                var raw = chunk.CreateRawChunk();
                                chunk.ParseFromRaw(raw);

                                var data = encoding.GetString(raw.Data).Split('\0');
                                var key = data.FirstOrDefault();
                                var value = string.Empty;
                                if (chunk.Id.Equals("zTXt"))
                                {
                                    value = GzipBytesToText(raw.Data.Skip(key.Length + 2).SkipWhile(c => c == 0).ToArray());
                                    if ((raw.Data.Length > key.Length + 2) && string.IsNullOrEmpty(value)) value = "(Decodeing Error)";
                                }
                                else if (chunk.Id.Equals("iTXt"))
                                {
                                    var vs = raw.Data.Skip(key.Length + 1).ToArray();
                                    var compress_flag = vs[0];
                                    var compress_method = vs[1];
                                    var language_tag = string.Empty;
                                    var translate_tag = string.Empty;
                                    var text = string.Empty;

                                    if (vs[2] == 0 && vs[3] == 0)
                                        text = compress_flag == 1 ? GzipBytesToText(vs.SkipWhile(c => c == 0).ToArray()) : encoding.GetString(vs.SkipWhile(c => c == 0).ToArray());
                                    else if (vs[2] == 0 && vs[3] != 0)
                                    {
                                        var trans = vs.Skip(3).TakeWhile(c => c != 0);
                                        translate_tag = encoding.GetString(trans.ToArray());

                                        var txt = vs.Skip(3).Skip(trans.Count()).SkipWhile(c => c == 0);
                                        text = compress_flag == 1 ? GzipBytesToText(txt.SkipWhile(c => c == 0).ToArray()) : encoding.GetString(txt.ToArray());
                                    }

                                    value = full_field ? $"{(int)compress_flag}, {(int)compress_method}, {language_tag}, {translate_tag}, {text}" : text.Trim().Trim('\0');
                                }
                                else
                                    value = full_field ? string.Join(", ", data.Skip(1)) : data.Last().Trim().Trim('\0');

                                result[key] = value;
                            }
                        }
                        png_r.End();
                    }
                }
            }
            catch (Exception ex) { Log(ex.Message); }
            return (result);
        }

        private static Dictionary<string, string> GetPngMetaInfo(FileInfo fileinfo, Encoding encoding = default(Encoding), bool full_field = true)
        {
            var result = new Dictionary<string, string>();
            try
            {
                if (fileinfo.Exists && fileinfo.Length > 0)
                {
                    using (var msi = new MemoryStream(File.ReadAllBytes(fileinfo.FullName)))
                    {
                        result = GetPngMetaInfo(msi, encoding, full_field);
                    }
                }
            }
            catch (Exception ex) { Log(ex.Message); }
            return (result);
        }

        public static Dictionary<string, string> GetPngMetaInfo(FileInfo fileinfo, Encoding encoding = default(Encoding))
        {
            var result = new Dictionary<string, string>();
            try
            {
                string[] png_meta_chunk_text = new string[]{ "iTXt", "tEXt", "zTXt" };
                if (fileinfo.Exists && fileinfo.Length > 0)
                {
                    if (encoding == default(Encoding)) encoding = Encoding.UTF8;
                    var png_r  = Hjg.Pngcs.FileHelper.CreatePngReader(fileinfo.FullName);
                    if (png_r is Hjg.Pngcs.PngReader)
                    {
                        png_r.ChunkLoadBehaviour = Hjg.Pngcs.Chunks.ChunkLoadBehaviour.LOAD_CHUNK_ALWAYS;
                        var png_chunks = png_r.GetChunksList();
                        foreach (var chunk in png_chunks.GetChunks())
                        {
                            if (png_meta_chunk_text.Contains(chunk.Id))
                            {
                                var raw = chunk.CreateRawChunk();
                                chunk.ParseFromRaw(raw);

                                var data = encoding.GetString(raw.Data).Split('\0');
                                var key = data.FirstOrDefault();
                                var value = string.Empty;
                                if (chunk.Id.Equals("zTXt"))
                                {
                                    value = GzipBytesToText(raw.Data.Skip(key.Length + 2).ToArray());
                                    if ((raw.Data.Length > key.Length + 2) && string.IsNullOrEmpty(value)) value = "(Decodeing Error)";
                                }
                                else if (chunk.Id.Equals("iTXt"))
                                {
                                    var vs = raw.Data.Skip(key.Length+1).ToArray();
                                    var compress_flag = vs[0];
                                    var compress_method = vs[1];
                                    var language_tag = string.Empty;
                                    var translate_tag = string.Empty;
                                    var text = string.Empty;

                                    if (vs[2] == 0 && vs[3] == 0)
                                        text = compress_flag == 1 ? GzipBytesToText(vs.Skip(4).ToArray()) : encoding.GetString(vs.Skip(4).ToArray());
                                    else if (vs[2] == 0 && vs[3] != 0)
                                    {
                                        var trans = vs.Skip(3).TakeWhile(c => c != 0);
                                        translate_tag = encoding.GetString(trans.ToArray());

                                        var txt = vs.Skip(3).Skip(trans.Count()).SkipWhile(c => c==0);
                                        text = compress_flag == 1 ? GzipBytesToText(txt.ToArray()) : encoding.GetString(txt.ToArray());
                                    }

                                    value = $"{(int)compress_flag}, {(int)compress_method}, {language_tag}, {translate_tag}, {text}";
                                }
                                else
                                    value = string.Join(", ", data.Skip(1));

                                result[key] = value;
                            }
                        }
                        png_r.End();
                    }
                }
            }
            catch (Exception ex) { Log(ex.Message); }
            return (result);
        }

        private static bool PngUpdateTextMetadata(string fileName, Dictionary<string, string> metainfo, bool keeptime = false)
        {
            var result = false;
            if (File.Exists(fileName) && metainfo is Dictionary<string, string>)
            {
                var fileinfo = new FileInfo(fileName);
                var dtc = fileinfo.CreationTime;
                var dtm = fileinfo.LastWriteTime;
                var dta = fileinfo.LastAccessTime;
                using (var mso = new MemoryStream())
                {
                    //using (var msi = Hjg.Pngcs.FileHelper.OpenFileForReading(fileName))
                    using (var msi = new MemoryStream(File.ReadAllBytes(fileName)))
                    {
                        if (msi.Length > 0)
                        {
                            var png_r = new Hjg.Pngcs.PngReader(msi);
                            if (png_r is Hjg.Pngcs.PngReader)
                            {
                                png_r.SetCrcCheckDisabled();
                                png_r.ChunkLoadBehaviour = Hjg.Pngcs.Chunks.ChunkLoadBehaviour.LOAD_CHUNK_ALWAYS;
                                var png_w = new Hjg.Pngcs.PngWriter(mso, png_r.ImgInfo);
                                if (png_w is Hjg.Pngcs.PngWriter)
                                {
                                    png_w.ShouldCloseStream = false;
                                    png_w.CopyChunksFirst(png_r, Hjg.Pngcs.Chunks.ChunkCopyBehaviour.COPY_ALL);

                                    var meta = png_w.GetMetadata();
                                    foreach (var kv in metainfo)
                                    {
                                        if (kv.Key.Equals(Hjg.Pngcs.Chunks.PngChunkTextVar.KEY_Creation_Time))
                                        {
                                            var chunk_ct = new Hjg.Pngcs.Chunks.PngChunkTEXT(png_r.ImgInfo);
                                            chunk_ct.SetKeyVal(kv.Key, kv.Value);
                                            chunk_ct.Priority = true;
                                            meta.QueueChunk(chunk_ct);
                                        }
                                        else
                                        {
                                            var chunk = meta.SetText(kv.Key, kv.Value);
                                            chunk.Priority = true;
                                        }
                                    }

                                    for (int row = 0; row < png_r.ImgInfo.Rows; row++)
                                    {
                                        Hjg.Pngcs.ImageLine il = png_r.ReadRow(row);
                                        png_w.WriteRow(il, row);
                                    }

                                    png_w.End();
                                }
                                png_r.End();
                            }
                        }
                    }
                    File.WriteAllBytes(fileName, mso.ToArray());
                }
                if (keeptime)
                {
                    fileinfo.CreationTime = dtc;
                    fileinfo.LastWriteTime = dtm;
                    fileinfo.LastAccessTime = dta;
                }
                result = true;
            }
            return (result);
        }

        public static bool UpdatePngMetaInfo(FileInfo fileinfo, DateTime? dt = null, MetaInfo meta = null)
        {
            var result = false;
            try
            {
                if (fileinfo.Exists && fileinfo.Length > 0 && meta is MetaInfo)
                {
                    var metainfo = new Dictionary<string, string>();

                    metainfo[Hjg.Pngcs.Chunks.PngChunkTextVar.KEY_Creation_Time] = (meta.DateTaken ?? dt ?? DateTime.Now).ToString("yyyy:MM:dd HH:mm:sszzz");
                    metainfo[Hjg.Pngcs.Chunks.PngChunkTextVar.KEY_Title] = string.IsNullOrEmpty(meta.Title) ? "" : meta.Title;
                    metainfo[Hjg.Pngcs.Chunks.PngChunkTextVar.KEY_Source] = string.IsNullOrEmpty(meta.Subject) ? "" : meta.Subject;
                    metainfo[Hjg.Pngcs.Chunks.PngChunkTextVar.KEY_Comment] = string.IsNullOrEmpty(meta.Keywords) ? "" : meta.Keywords;
                    metainfo[Hjg.Pngcs.Chunks.PngChunkTextVar.KEY_Description] = string.IsNullOrEmpty(meta.Comment) ? "" : meta.Comment;
                    metainfo[Hjg.Pngcs.Chunks.PngChunkTextVar.KEY_Author] = string.IsNullOrEmpty(meta.Authors) ? "" : meta.Authors;
                    metainfo[Hjg.Pngcs.Chunks.PngChunkTextVar.KEY_Copyright] = string.IsNullOrEmpty(meta.Authors) ? "" : meta.Authors;
                    //Hjg.Pngcs.Chunks.PngChunkTextVar.KEY_Software
                    //Hjg.Pngcs.Chunks.PngChunkTextVar.KEY_Disclaimer
                    //Hjg.Pngcs.Chunks.PngChunkTextVar.KEY_Warning

                    //result  = Hjg.Pngcs.FileHelper.PngUpdateTextMetadata(fileinfo.FullName, metainfo, keeptime: true);
                    result = PngUpdateTextMetadata(fileinfo.FullName, metainfo, keeptime: true);
                }
            }
            catch (Exception ex) { Log(ex.Message); }
            return (result);
        }
        #endregion

        public static void TouchProfile(string file, Dictionary<string, IImageProfile> profiles, bool force = false)
        {
            if (force && File.Exists(file) && profiles is Dictionary<string, IImageProfile>)
            {
                try
                {
                    var fi = new FileInfo(file);
                    var exifdata = new CompactExifLib.ExifData(fi.FullName);
                    using (MagickImage image = new MagickImage(fi.FullName))
                    {
                        if (image.Endian == Endian.Undefined) image.Endian = exifdata.ByteOrder == ExifByteOrder.BigEndian ? Endian.MSB : Endian.LSB;
                        TouchProfile(image, profiles, force);
                    }
                }
                catch (Exception ex) { Log(ex.Message); }
            }
        }

        public static void TouchProfile(MagickImage image, Dictionary<string, IImageProfile> profiles, bool force = false)
        {
            if (force && image is MagickImage && image.FormatInfo.IsWritable && profiles is Dictionary<string, IImageProfile>)
            {
                foreach (var kv in profiles)
                {
                    try
                    {
                        var profile_name = kv.Key;
                        var profile = kv.Value;
                        if (force || !image.HasProfile(profile_name))
                        {
                            var old_size = image.HasProfile(profile_name) ? image.GetProfile(profile_name).GetData().Length : 0;
                            image.SetProfile(profile);
                            Log($"{$"Profile {profile_name}".PadRight(32)}= {(old_size == 0 ? "NULL" : $"{old_size}")} => {profile.GetData().Length} Bytes");
                        }
                        else
                        {
                            if (profile is ExifProfile)
                            {
                                var exif_old = image.GetExifProfile();
                                var tags_old = exif_old.Values.Select(v => v.Tag);
                                var exif_new = profile as ExifProfile;
                                exif_new.Parts = ExifParts.ExifTags | ExifParts.IfdTags;
                                foreach (IExifValue value in exif_new.Values)
                                {
                                    if (!tags_old.Contains(value.Tag) && value.GetValue() != null) value.SetValue(value.GetValue());
                                }
                            }
                            else if (profile is IptcProfile)
                            {
                                var iptc_old = image.GetIptcProfile();
                                var tags_old = iptc_old.Values.Select(v => v.Tag);
                                var iptc_new = profile as IptcProfile;
                                foreach (IIptcValue value in iptc_new.Values)
                                {
                                    if (!tags_old.Contains(value.Tag) && value.Value != null) iptc_old.SetValue(value.Tag, value.Value);
                                }
                            }
                        }
                    }
                    catch (Exception ex) { Log(ex.Message); }
                }
            }
        }

        public static void TouchAttribute(string file, Dictionary<string, string> attrs, bool force = false)
        {
            if (force && File.Exists(file) && attrs is Dictionary<string, string>)
            {
                try
                {
                    var fi = new FileInfo(file);
                    var exifdata = new CompactExifLib.ExifData(fi.FullName);
                    using (MagickImage image = new MagickImage(fi.FullName))
                    {
                        if (image.Endian == Endian.Undefined) image.Endian = exifdata.ByteOrder == ExifByteOrder.BigEndian ? Endian.MSB : Endian.LSB;
                        TouchAttribute(image, attrs, force);
                    }
                }
                catch (Exception ex) { Log(ex.Message); }
            }
        }

        public static void TouchAttribute(MagickImage image, Dictionary<string, string> attrs, bool force = false)
        {
            if (force && image is MagickImage && image.FormatInfo.IsWritable && attrs is Dictionary<string, string>)
            {
                foreach (var kv in attrs)
                {
                    try
                    {
                        var attr = kv.Key;
                        if (attr.StartsWith("date:")) continue;
                        if (force || !image.AttributeNames.Contains(attr))
                        {
                            var old_value = image.AttributeNames.Contains(attr) ?  GetAttribute(image, attr) : "NULL";
                            var value = kv.Value;
                            SetAttribute(image, attr, value);
                            Log($"{$"{attr}".PadRight(32)}= {old_value} => {value}");
                        }
                    }
                    catch (Exception ex) { Log(ex.Message); }
                }
            }
        }

        public static DateTime? GetMetaTime(MagickImage image)
        {
            DateTime? result = null;
            try
            {
                if (image is MagickImage && image.FormatInfo.IsReadable)
                {
                    foreach (var tag in tag_date)
                    {
                        if (image.AttributeNames.Contains(tag))
                        {
                            var v = image.GetAttribute(tag);
                            var nv = Regex.Replace(v, @"^(\d{4}):(\d{2}):(\d{2})[ |T](.*?)Z?$", "$1-$2-$3T$4");
                            //Log($"{tag.PadRight(32)}= {v} > {nv}");
                            result = DateTime.Parse(tag.Contains("png") ? nv.Substring(0, tag.Length - 1) : nv);
                            break;
                        }
                    }
                }
            }
            catch (Exception ex) { Log(ex.Message); }
            return (result);
        }

        public static DateTime? GetMetaTime(string file)
        {
            DateTime? result = null;
            if (File.Exists(file))
            {
                var fi = new FileInfo(file);
                using (var ms = new FileStream(fi.FullName, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    try
                    {
                        using (MagickImage image = new MagickImage(ms))
                        {
                            result = GetMetaTime(image);
                        }
                    }
                    catch (Exception ex) { Log(ex.Message); }
                }
            }
            else Log($"File \"{file}\" not exists!");
            return (result);
        }

        public static MetaInfo GetMetaInfo(MagickImage image)
        {
            MetaInfo result = new MetaInfo();

            if (image is MagickImage && image.FormatInfo.IsReadable)
            {
                #region EXIF, XMP Profiles
                if (image.AttributeNames.Count() > 0)
                {
                    result.Attributes = new Dictionary<string, string>();
                    foreach (var attr in image.AttributeNames) { try { result.Attributes.Add(attr, GetAttribute(image, attr)); } catch { } }
                }
                if (image.ProfileNames.Count() > 0)
                {
                    result.Profiles = new Dictionary<string, IImageProfile>();
                    foreach (var profile in image.ProfileNames) { try { result.Profiles.Add(profile, image.GetProfile(profile)); } catch { } }
                }
                var exif = image.HasProfile("exif") ? image.GetExifProfile() : new ExifProfile();
                var xmp = image.HasProfile("xmp") ? image.GetXmpProfile() : null;
                #endregion

                bool is_png = image.FormatInfo.MimeType.Equals("image/png");
                #region Datetime
                result.DateAcquired = GetMetaTime(image);
                result.DateTaken = result.DateAcquired;
                #endregion
                #region Title
                foreach (var tag in tag_title)
                {
                    if (image.AttributeNames.Contains(tag))
                    {
                        result.Title = GetAttribute(image, tag);
                        if (!string.IsNullOrEmpty(result.Title)) break;
                    }
                }
                #endregion
                #region Subject
                foreach (var tag in tag_subject)
                {
                    if (image.AttributeNames.Contains(tag))
                    {
                        result.Subject = GetAttribute(image, tag);
                        if (!string.IsNullOrEmpty(result.Subject)) break;
                    }
                }
                #endregion
                #region Comment
                foreach (var tag in tag_comments)
                {
                    if (image.AttributeNames.Contains(tag))
                    {
                        result.Comment = GetAttribute(image, tag);
                        if (!string.IsNullOrEmpty(result.Comment)) break;
                    }
                }
                #endregion
                #region Keywords
                foreach (var tag in tag_keywords)
                {
                    if (image.AttributeNames.Contains(tag))
                    {
                        result.Keywords = GetAttribute(image, tag);
                        if (!string.IsNullOrEmpty(result.Keywords)) break;
                    }
                }
                #endregion
                #region Authors
                foreach (var tag in tag_author)
                {
                    if (image.AttributeNames.Contains(tag))
                    {
                        result.Authors = GetAttribute(image, tag);
                        if (!string.IsNullOrEmpty(result.Authors)) break;
                    }
                }
                #endregion
                #region Copyright
                foreach (var tag in tag_copyright)
                {
                    if (image.AttributeNames.Contains(tag))
                    {
                        result.Copyrights = GetAttribute(image, tag);
                        if (!string.IsNullOrEmpty(result.Copyrights)) break;
                    }
                }
                #endregion
                #region Rating
                foreach (var tag in tag_rating)
                {
                    try
                    {
                        if (image.AttributeNames.Contains(tag))
                        {
                            if (tag.Equals("Rating"))
                            {
                                result.Rating = Convert.ToInt32(GetAttribute(image, tag));
                                result.RatingPercent = RankingToRating(result.Rating);
                            }
                            else if (tag.Equals("RatingPercent"))
                            {
                                result.RatingPercent = Convert.ToInt32(GetAttribute(image, tag));
                                result.Rating = RatingToRanking(result.RatingPercent);
                            }
                            else if (tag.Equals("xmp:Rating"))
                            {
                                result.Rating = Convert.ToInt32(GetAttribute(image, tag));
                                result.RatingPercent = RankingToRating(result.Rating);
                            }
                            else if (tag.Equals("MicrosoftPhoto:Rating"))
                            {
                                result.RatingPercent = Convert.ToInt32(GetAttribute(image, tag));
                                result.Rating = RatingToRanking(result.RatingPercent);
                            }
                        }
                    }
                    catch (Exception ex) { Log(ex.Message); }
                }
                #endregion
            }
            return (result);
        }

        public static MetaInfo GetMetaInfo(string file)
        {
            MetaInfo result = new MetaInfo();
            if (File.Exists(file))
            {
                var fi = new FileInfo(file);
                result.DateAcquired = fi.CreationTime;
                result.DateTaken = fi.LastWriteTime;
                using (var ms = new FileStream(fi.FullName, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    try
                    {
                        var exifdata = new CompactExifLib.ExifData(ms);
                        ms.Seek(0, SeekOrigin.Begin);

                        using (MagickImage image = new MagickImage(ms))
                        {
                            if (image.Endian == Endian.Undefined) image.Endian = exifdata.ByteOrder == ExifByteOrder.BigEndian ? Endian.MSB : Endian.LSB;
                            result = GetMetaInfo(image);
                        }

                        if (exifdata.TagExists(CompactExifLib.ExifTag.Rating))
                        {
                            int ranking;
                            if (exifdata.GetTagValue(CompactExifLib.ExifTag.Rating, out ranking) && ranking != result.Rating)
                                result.Rating = result.Rating ?? ranking;
                            result.RatingPercent = RankingToRating(result.Rating);
                            int rating;
                            if (exifdata.GetTagValue(CompactExifLib.ExifTag.RatingPercent, out rating) && rating != result.RatingPercent)
                                result.RatingPercent = result.RatingPercent ?? rating;
                            result.Rating = RatingToRanking(result.RatingPercent);
                        }
                        if (exifdata.TagExists(CompactExifLib.ExifTag.XmpMetadata))
                        {
                            CompactExifLib.ExifTagType type;
                            int bytecounts;
                            byte[] bytes;
                            if (exifdata.GetTagRawData(CompactExifLib.ExifTag.XmpMetadata, out type, out bytecounts, out bytes))
                            {
                                if (!result.Profiles.ContainsKey("xmp"))
                                    result.Profiles["xmp"] = new XmpProfile(bytes);
                            }
                        }

                    }
                    catch (Exception ex) { Log(ex.Message); }
                }
            }
            else Log($"File \"{file}\" not exists!");
            return (result);
        }

        private static MetaInfo XmlToMeta(XmlDocument xml, MetaInfo meta = null)
        {
            MetaInfo result = meta is MetaInfo ? meta : new MetaInfo();
            if (xml is XmlDocument)
            {
                XmlDocument node = xml;
                if (xml.GetElementsByTagName("illust").Count > 0)
                {
                    node = new XmlDocument();
                    node.LoadXml(xml.InnerXml);
                    node.DocumentElement.RemoveAll();
                    var child = node.ImportNode(xml.GetElementsByTagName("illust")[0], true);
                    node.DocumentElement.AppendChild(child);
                }
                var id = node.GetElementsByTagName("id").Count > 0 ? xml.GetElementsByTagName("id")[0].InnerText : string.Empty;
                var date = node.GetElementsByTagName("date").Count > 0 ? xml.GetElementsByTagName("date")[0].InnerText : string.Empty;
                var title = node.GetElementsByTagName("title").Count > 0 ? xml.GetElementsByTagName("title")[0].InnerText : string.Empty;
                var subject = node.GetElementsByTagName("subject").Count > 0 ? xml.GetElementsByTagName("subject")[0].InnerText : string.Empty;
                var desc = xml.GetElementsByTagName("description").Count > 0 ? xml.GetElementsByTagName("description")[0].InnerText : string.Empty;
                var tags = xml.GetElementsByTagName("tags").Count > 0 ? xml.GetElementsByTagName("tags")[0].InnerText : string.Empty;
                var favor = xml.GetElementsByTagName("favorited").Count > 0 ? xml.GetElementsByTagName("favorited")[0].InnerText : string.Empty;
                var down = xml.GetElementsByTagName("downloaded").Count > 0 ? xml.GetElementsByTagName("downloaded")[0].InnerText : string.Empty;
                var link = xml.GetElementsByTagName("weblink").Count > 0 ? xml.GetElementsByTagName("weblink")[0].InnerText : $"https://www.pixiv.net/artworks/{id}";
                var user = xml.GetElementsByTagName("user").Count > 0 ? xml.GetElementsByTagName("user")[0].InnerText : string.Empty;
                var uid = xml.GetElementsByTagName("userid").Count > 0 ? xml.GetElementsByTagName("userid")[0].InnerText : string.Empty;
                var ulink = xml.GetElementsByTagName("userlink").Count > 0 ? xml.GetElementsByTagName("userlink")[0].InnerText : string.Empty;
                var author = xml.GetElementsByTagName("author").Count > 0 ? xml.GetElementsByTagName("author")[0].InnerText : string.Empty;
                var copyright = xml.GetElementsByTagName("copyright").Count > 0 ? xml.GetElementsByTagName("copyright")[0].InnerText : string.Empty;

                DateTime dt = result.DateAcquired ?? result.DateTaken ?? result.DateModified ?? result.DateCreated ?? result.DateAccesed ?? DateTime.Now;
                if (DateTime.TryParse(date, out dt))
                {
                    result.DateCreated = dt;
                    result.DateModified = dt;
                    result.DateAccesed = dt;

                    result.DateAcquired = dt;
                    result.DateTaken = dt;
                }
                result.Title = title;
                result.Subject = string.IsNullOrEmpty(subject) ? link : subject;
                result.Keywords = tags;
                result.Comment = string.IsNullOrEmpty(subject) ? desc : (string.IsNullOrEmpty(id) ? desc : $"{desc}{Environment.NewLine}{Environment.NewLine}{link}");
                result.Authors = string.IsNullOrEmpty(author) ? string.IsNullOrEmpty(uid) ? $"{user}" : $"{user}; uid:{uid}" : author;
                result.Copyrights = string.IsNullOrEmpty(copyright) ? result.Authors : copyright;

                bool fav = false;
                if (bool.TryParse(favor, out fav)) result.RatingPercent = fav ? 75 : 0;
            }
            return (result);
        }

        public static MetaInfo GetMetaInfoFromClipboard(MetaInfo meta)
        {
            var result = meta;
            try
            {
                var log = new List<string>();
                var dp = Clipboard.GetDataObject();
                var fmts = dp.GetFormats();
                foreach (var fmt in fmts)
                {
                    try
                    {
                        if (fmt.Equals("xmldocument", StringComparison.CurrentCultureIgnoreCase) && dp.GetDataPresent(fmt, true))
                        {
                            var xmldoc = dp.GetData(fmt, true);
                            if (xmldoc is XmlDocument)
                            {
                                result = XmlToMeta(xmldoc as XmlDocument, result);
                                log.Add($"{fmt.PadRight(16)} : Get Metadata Successed!");
                                break;
                            }
                        }
                        else if (fmt.Equals("xml", StringComparison.CurrentCultureIgnoreCase) && dp.GetDataPresent(fmt, true))
                        {
                            var xmldoc = dp.GetData(fmt, true);
                            if (xmldoc is string && !string.IsNullOrEmpty(xmldoc as string))
                            {
                                var xml = new XmlDocument();
                                xml.LoadXml(xmldoc as string);
                                result = XmlToMeta(xml, result);
                                log.Add($"{fmt.PadRight(16)} : Get Metadata Successed!");
                                break;
                            }
                        }
                        else if (new string[] { "PixivIllustJson", "PixivIllustJSON", "JSON", "Text" }.Contains(fmt) && dp.GetDataPresent(fmt, true))
                        {
                            var json = dp.GetData(fmt, true);

                            log.Add($"{fmt.PadRight(16)} : Get Metadata Successed!");
                            //break;
                        }
                    }
                    catch (Exception ex) { log.Add($"{fmt.PadRight(16)} : {ex.Message}"); }
                }
                if (log.Count > 0) ShowMessage(string.Join(Environment.NewLine, log), "Get Metadata From Clipboard");
            }
            catch (Exception ex) { ShowMessage(ex.Message); }
            return (result);
        }

        public static void SetMetaInfoToClipboard(MetaInfo meta)
        {
            try
            {
                var is_fav = meta.Rating >= 4;
                var dm = meta.DateTaken ?? meta.DateAcquired ?? meta.DateModified ?? meta.DateCreated ?? null;
                var dm_str = dm.HasValue ? dm.Value.ToString("yyyy-MM-ddTHH:mm:ss") : string.Empty;

                var sb_json = new StringBuilder();
                sb_json.AppendLine("{");
                sb_json.AppendLine($"  \"date\": \"{dm_str}\",");
                sb_json.AppendLine($"  \"title\": \"{meta.Title}\",");
                sb_json.AppendLine($"  \"subject\": \"{meta.Subject}\",");
                sb_json.AppendLine($"  \"tags\": \"{meta.Keywords}\",");
                sb_json.AppendLine($"  \"description\": \"{meta.Comment}\",");
                sb_json.AppendLine($"  \"user\": \"{meta.Authors}\",");
                sb_json.AppendLine($"  \"copyright\": \"{meta.Authors}\",");
                sb_json.AppendLine($"  \"favorited\": {is_fav.ToString()},");
                sb_json.AppendLine("}");
                var json = sb_json.ToString();

                var sb_xml = new StringBuilder();
                sb_xml.AppendLine("<?xml version='1.0' standalone='no'?>");
                sb_xml.AppendLine("<root>");
                sb_xml.AppendLine($"  <date>{dm_str}</date>");
                sb_xml.AppendLine($"  <title>{meta.Title}</title>");
                sb_xml.AppendLine($"  <subject>{meta.Subject}</subject>");
                sb_xml.AppendLine($"  <tags>{meta.Keywords}</tags>");
                sb_xml.AppendLine($"  <description>{meta.Comment}</description>");
                sb_xml.AppendLine($"  <user>{meta.Authors}</user>");
                sb_xml.AppendLine($"  <copyright>{meta.Authors}</copyright>");
                sb_xml.AppendLine($"  <favorited>{is_fav}</favorited>");
                sb_xml.AppendLine("</root>");
                var xml = sb_xml.ToString();

                var dataObject = new DataObject();
                dataObject.SetData("Xml", xml);
                dataObject.SetData("PixivIllustJSON", json);
                dataObject.SetData("PixivJSON", json);
                dataObject.SetData("JSON", json);
                dataObject.SetData(DataFormats.Text, json);
                dataObject.SetData(DataFormats.UnicodeText, json);
                Clipboard.SetDataObject(dataObject, true);
            }
            catch (Exception ex) { ShowMessage(ex.Message); }
        }
        #endregion

        #region Metadata Oprating
        public static void ClearMeta(string file)
        {
            if (File.Exists(file))
            {
                var fi = new FileInfo(file);
                var dc = fi.CreationTime;
                var dm = fi.LastWriteTime;
                var da = fi.LastAccessTime;
#if DEBUG
                var exifdata = new CompactExifLib.ExifData(fi.FullName);
#endif
                using (MagickImage image = new MagickImage(fi.FullName))
                {
                    if (image.FormatInfo.IsReadable && image.FormatInfo.IsWritable)
                    {
#if DEBUG
                        if (image.Endian == Endian.Undefined) image.Endian = exifdata.ByteOrder == ExifByteOrder.BigEndian ? Endian.MSB : Endian.LSB;
#endif
                        foreach (var attr in image.AttributeNames)
                        {
                            try
                            {
                                Log($"Try remove attribute '{attr}' ...");
                                image.RemoveAttribute(attr);
                            }
                            catch (Exception ex) { Log(ex.Message); }
                        }
                        foreach (var profile_name in image.ProfileNames)
                        {
                            try
                            {
                                Log($"Try remove profile '{profile_name}' ...");
                                image.RemoveProfile(image.GetProfile(profile_name));
                            }
                            catch (Exception ex) { Log(ex.Message); }
                        }

                        FixDPI(image);
                        image.Write(fi.FullName, image.Format);
                    }
                    else
                    {
                        if (!image.FormatInfo.IsReadable)
                            Log($"File \"{file}\" is not a read supported format!");
                        if (!image.FormatInfo.IsWritable)
                            Log($"File \"{file}\" is not a write supported format!");
                    }
                }

                fi.CreationTime = dc;
                fi.LastWriteTime = dm;
                fi.LastAccessTime = da;
            }
            else Log($"File \"{file}\" not exists!");
        }

        public static void TouchMeta(string file, bool force = false, DateTime? dtc = null, DateTime? dtm = null, DateTime? dta = null, MetaInfo meta = null)
        {
            if (meta.ChangeProperties == ChangePropertyType.None)
            {
                Log($"File \"{file}\" needn't do anything!");
                return;
            }
            if (File.Exists(file))
            {
                try
                {
                    var fi = new FileInfo(file);

                    var title = meta is MetaInfo ? meta.Title ?? Path.GetFileNameWithoutExtension(fi.Name) : Path.GetFileNameWithoutExtension(fi.Name);
                    var subject = meta is MetaInfo ? meta.Subject : title;
                    var authors = meta is MetaInfo ? meta.Authors : string.Empty;
                    var copyrights = meta is MetaInfo ? meta.Copyrights : authors;
                    var keywords = meta is MetaInfo ? meta.Keywords : string.Empty;
                    var comment = meta is MetaInfo ? meta.Comment : string.Empty;
                    var rating = meta is MetaInfo ? meta.RatingPercent : null;
                    if (!string.IsNullOrEmpty(title)) title.Replace("\0", string.Empty).TrimEnd('\0');
                    if (!string.IsNullOrEmpty(subject)) subject.Replace("\0", string.Empty).TrimEnd('\0');
                    if (!string.IsNullOrEmpty(authors)) authors.Replace("\0", string.Empty).TrimEnd('\0');
                    if (!string.IsNullOrEmpty(copyrights)) copyrights.Replace("\0", string.Empty).TrimEnd('\0');
                    if (!string.IsNullOrEmpty(keywords)) keywords.Replace("\0", string.Empty).TrimEnd('\0');
                    if (!string.IsNullOrEmpty(comment)) comment.Replace("\0", string.Empty).TrimEnd('\0');

                    var keyword_list = string.IsNullOrEmpty(keywords) ? new List<string>() : keywords.Split(new char[] { ';', '#' }, StringSplitOptions.RemoveEmptyEntries).Select(k => k.Trim()).Where(k => !string.IsNullOrEmpty(k)).Distinct();
                    keywords = string.Join("; ", keyword_list);

                    var exifdata = new CompactExifLib.ExifData(fi.FullName);
                    using (MagickImage image = new MagickImage(fi.FullName))
                    {
                        if (image.FormatInfo.IsReadable && image.FormatInfo.IsWritable)
                        {
                            if (image.Endian == Endian.Undefined) image.Endian = exifdata.ByteOrder == ExifByteOrder.BigEndian ? Endian.MSB : Endian.LSB;

                            bool is_png = image.FormatInfo.MimeType.Equals("image/png", StringComparison.CurrentCultureIgnoreCase);
                            bool is_jpg = image.FormatInfo.MimeType.Equals("image/jpeg", StringComparison.CurrentCultureIgnoreCase);

                            #region touch attributes and profiles
                            if (meta is MetaInfo && meta.TouchProfiles)
                            {
                                if (meta.Profiles != null && meta.Profiles.Count > 0) TouchProfile(image, meta.Profiles, force);
                                if (meta.Attributes != null && meta.Attributes.Count > 0) TouchAttribute(image, meta.Attributes, force);
                            }
                            #endregion

                            var exif = image.HasProfile("exif") ? image.GetExifProfile() : new ExifProfile();
                            var xmp = image.HasProfile("xmp") ? image.GetXmpProfile() : null;

                            #region touch date
                            var dc = dtc ?? (meta is MetaInfo ? meta.DateAcquired ?? meta.DateTaken ?? meta.DateCreated : null) ?? fi.CreationTime;
                            var dm = dtm ?? (meta is MetaInfo ? meta.DateAcquired ?? meta.DateTaken ?? meta.DateModified : null) ?? fi.LastWriteTime;
                            var da = dta ?? (meta is MetaInfo ? meta.DateAcquired ?? meta.DateTaken ?? meta.DateAccesed : null) ?? fi.LastAccessTime;

                            if (!force)
                            {
                                var dt = GetMetaTime(image);
                                dc = dt ?? dtc ?? (meta is MetaInfo ? meta.DateAcquired ?? meta.DateTaken ?? meta.DateCreated : null) ?? fi.CreationTime;
                                dm = dt ?? dtm ?? (meta is MetaInfo ? meta.DateAcquired ?? meta.DateTaken ?? meta.DateModified : null) ?? fi.LastWriteTime;
                                da = dt ?? dta ?? (meta is MetaInfo ? meta.DateAcquired ?? meta.DateTaken ?? meta.DateAccesed : null) ?? fi.LastAccessTime;
                            }

                            // 2021:09:13 11:00:16
                            var dc_exif = dc.ToString("yyyy:MM:dd HH:mm:ss");
                            var dm_exif = dm.ToString("yyyy:MM:dd HH:mm:ss");
                            var da_exif = da.ToString("yyyy:MM:dd HH:mm:ss");
                            // 2021:09:13T11:00:16
                            var dc_xmp = dc.ToString("yyyy:MM:dd HH:mm:ss");
                            var dm_xmp = dm.ToString("yyyy:MM:dd HH:mm:ss");
                            var da_xmp = da.ToString("yyyy:MM:dd HH:mm:ss");
                            // 2021-09-13T06:38:49+00:00
                            var dc_date = dc.ToString("yyyy-MM-ddTHH:mm:sszzz");
                            var dm_date = dm.ToString("yyyy-MM-ddTHH:mm:sszzz");
                            var da_date = da.ToString("yyyy-MM-ddTHH:mm:sszzz");
                            // 2021-08-26T12:23:49
                            var dc_ms = dc.ToString("yyyy-MM-ddTHH:mm:sszzz");
                            var dm_ms = dm.ToString("yyyy-MM-ddTHH:mm:sszzz");
                            var da_ms = da.ToString("yyyy-MM-ddTHH:mm:sszzz");
                            // 2021-08-26T12:23:49.002
                            var dc_msxmp = dc.ToString("yyyy-MM-ddTHH:mm:ss.fff");
                            var dm_msxmp = dm.ToString("yyyy-MM-ddTHH:mm:ss.fff");
                            var da_msxmp = da.ToString("yyyy-MM-ddTHH:mm:ss.fff");
                            // 2021-09-13T08:38:13Z
                            var dc_png = dc.ToString("yyyy-MM-ddTHH:mm:ssZ");
                            var dm_png = dm.ToString("yyyy-MM-ddTHH:mm:ssZ");
                            var da_png = da.ToString("yyyy-MM-ddTHH:mm:ssZ");
                            // 2021:09:13 11:00:16+08:00
                            var dc_misc = dc.ToString("yyyy:MM:dd HH:mm:sszzz");
                            var dm_misc = dm.ToString("yyyy:MM:dd HH:mm:sszzz");
                            var da_misc = da.ToString("yyyy:MM:dd HH:mm:sszzz");

                            if (meta is MetaInfo && meta.ChangeProperties.HasFlag(ChangePropertyType.DateTime))
                            {
                                if (image.AttributeNames.Contains("exif:DateTime"))
                                {
                                    var edt = GetAttribute(image, "exif:DateTime");
                                    if (!edt.Equals(dm_exif)) image.RemoveAttribute("exif:DateTime");
                                }
                                foreach (var tag in tag_date)
                                {
                                    try
                                    {
                                        if (force || !image.AttributeNames.Contains(tag))
                                        {
                                            var value_old = image.GetAttribute(tag);

                                            if (tag.StartsWith("date")) SetAttribute(image, tag, dm_date);
                                            else if (tag.StartsWith("exif")) SetAttribute(image, tag, dm_exif);
                                            else if (tag.StartsWith("png")) { image.RemoveAttribute(tag); SetAttribute(image, tag, dm_png); }
                                            else if (tag.StartsWith("tiff")) { image.RemoveAttribute(tag); SetAttribute(image, tag, dm_date); }
                                            else if (tag.StartsWith("Microsoft")) { image.RemoveAttribute(tag); SetAttribute(image, tag, dm_ms); }
                                            else if (tag.StartsWith("xmp")) SetAttribute(image, tag, dm_xmp);
                                            else SetAttribute(image, tag, dm_misc);

                                            var value_new = GetAttribute(image, tag);
                                            Log($"{$"{tag}".PadRight(32)}= {(value_old == null ? "NULL" : value_old)} => {value_new}");
                                        }
                                    }
                                    catch (Exception ex) { Log(ex.Message); }
                                }
                            }
                            #endregion
                            #region touch title
                            if (meta is MetaInfo && meta.ChangeProperties.HasFlag(ChangePropertyType.Title))
                            {
                                foreach (var tag in tag_title)
                                {
                                    try
                                    {
                                        var value_old = GetAttribute(image, tag);
                                        if (force || (!image.AttributeNames.Contains(tag) && !string.IsNullOrEmpty(title)))
                                        {
                                            SetAttribute(image, tag, title);
                                            var value_new = GetAttribute(image, tag);
                                            Log($"{$"{tag}".PadRight(32)}= {(value_old == null ? "NULL" : value_old)} => {value_new}");
                                        }
                                        else
                                        {
                                            if (tag.Equals("exif:WinXP-Title"))
                                            {
                                                if (exif.GetValue(ImageMagick.ExifTag.XPTitle) == null)
                                                {
                                                    if (!string.IsNullOrEmpty(title)) SetAttribute(image, tag, value_old);
                                                }
                                                else title = GetAttribute(image, tag);
                                            }
                                            else if (tag.Equals("exif:ImageDescription"))
                                            {
                                                if (exif.GetValue(ImageMagick.ExifTag.ImageDescription) == null)
                                                {
                                                    if (!string.IsNullOrEmpty(title)) SetAttribute(image, tag, value_old);
                                                }
                                                else title = GetAttribute(image, tag);
                                            }
                                            else if (tag.Equals("iptc:Description"))
                                            {

                                            }
                                        }
                                    }
                                    catch (Exception ex) { Log(ex.Message); }
                                }
                            }
                            #endregion
                            #region touch subject
                            if (meta is MetaInfo && meta.ChangeProperties.HasFlag(ChangePropertyType.Subject))
                            {
                                foreach (var tag in tag_subject)
                                {
                                    try
                                    {
                                        var value_old = GetAttribute(image, tag);
                                        if (force || (!image.AttributeNames.Contains(tag) && !string.IsNullOrEmpty(subject)))
                                        {
                                            SetAttribute(image, tag, subject);
                                            var value_new = GetAttribute(image, tag);
                                            Log($"{$"{tag}".PadRight(32)}= {(value_old == null ? "NULL" : value_old)} => {value_new}");
                                        }
                                        else
                                        {
                                            if (tag.Equals("exif:WinXP-Subject"))
                                            {
                                                if (exif.GetValue(ImageMagick.ExifTag.XPSubject) == null)
                                                {
                                                    if (!string.IsNullOrEmpty(subject)) exif.SetValue(ImageMagick.ExifTag.XPSubject, Encoding.Unicode.GetBytes(value_old));
                                                }
                                                else subject = Encoding.Unicode.GetString(exif.GetValue(ImageMagick.ExifTag.XPSubject).Value);
                                            }
                                        }
                                    }
                                    catch (Exception ex) { Log(ex.Message); }
                                }
                            }
                            #endregion
                            #region touch author
                            if (meta is MetaInfo && meta.ChangeProperties.HasFlag(ChangePropertyType.Authors))
                            {
                                foreach (var tag in tag_author)
                                {
                                    try
                                    {
                                        var value_old = GetAttribute(image, tag);
                                        if (force || (!image.AttributeNames.Contains(tag) && !string.IsNullOrEmpty(authors)))
                                        {
                                            SetAttribute(image, tag, authors);
                                            var value_new = GetAttribute(image, tag);
                                            Log($"{$"{tag}".PadRight(32)}= {(value_old == null ? "NULL" : value_old)} => {value_new}");
                                        }
                                        else
                                        {
                                            if (tag.Equals("exif:WinXP-Author"))
                                            {
                                                if (exif.GetValue(ImageMagick.ExifTag.XPAuthor) == null)
                                                {
                                                    if (!string.IsNullOrEmpty(authors)) SetAttribute(image, tag, value_old);
                                                }
                                                else authors = GetAttribute(image, tag);
                                            }
                                            else if (tag.Equals("exif:Artist"))
                                            {
                                                if (exif.GetValue(ImageMagick.ExifTag.Artist) == null)
                                                {
                                                    if (!string.IsNullOrEmpty(authors)) SetAttribute(image, tag, value_old);
                                                }
                                                else authors = GetAttribute(image, tag);
                                            }
                                        }
                                    }
                                    catch (Exception ex) { Log(ex.Message); }
                                }
                            }
                            #endregion
                            #region touch copywright
                            if (meta is MetaInfo && meta.ChangeProperties.HasFlag(ChangePropertyType.Copyrights))
                            {
                                foreach (var tag in tag_copyright)
                                {
                                    try
                                    {
                                        var value_old = GetAttribute(image, tag);
                                        if (force || (!image.AttributeNames.Contains(tag) && !string.IsNullOrEmpty(copyrights)))
                                        {
                                            SetAttribute(image, tag, copyrights);
                                            var value_new = GetAttribute(image, tag);
                                            Log($"{$"{tag}".PadRight(32)}= {(value_old == null ? "NULL" : value_old)} => {value_new}");
                                        }
                                        else
                                        {
                                            if (tag.Equals("exif:Copyright"))
                                            {
                                                if (exif.GetValue(ImageMagick.ExifTag.Copyright) == null)
                                                {
                                                    if (!string.IsNullOrEmpty(copyrights)) SetAttribute(image, tag, value_old);
                                                }
                                                else copyrights = GetAttribute(image, tag);
                                            }
                                        }
                                    }
                                    catch (Exception ex) { Log(ex.Message); }
                                }
                            }
                            #endregion
                            #region touch comment
                            if (meta is MetaInfo && meta.ChangeProperties.HasFlag(ChangePropertyType.Comment))
                            {
                                foreach (var tag in tag_comments)
                                {
                                    try
                                    {
                                        var value_old = GetAttribute(image, tag);
                                        if (force || (!image.AttributeNames.Contains(tag) && !string.IsNullOrEmpty(comment)))
                                        {
                                            SetAttribute(image, tag, comment);
                                            var value_new = GetAttribute(image, tag);
                                            Log($"{$"{tag}".PadRight(32)}= {(value_old == null ? "NULL" : value_old)} => {value_new}");
                                        }
                                        else
                                        {
                                            if (tag.Equals("exif:WinXP-Comment"))
                                            {
                                                if (exif.GetValue(ImageMagick.ExifTag.XPComment) == null)
                                                {
                                                    if (!string.IsNullOrEmpty(comment)) SetAttribute(image, tag, value_old);
                                                }
                                                else comment = GetAttribute(image, tag);
                                            }
                                            else if (tag.Equals("exif:WinXP-Comments"))
                                            {
                                                if (exif.GetValue(ImageMagick.ExifTag.XPComment) == null)
                                                {
                                                    if (!string.IsNullOrEmpty(comment)) SetAttribute(image, tag, value_old);
                                                }
                                                else comment = GetAttribute(image, tag);
                                            }
                                            else if (tag.Equals("exif:UserComment"))
                                            {
                                                if (exif.GetValue(ImageMagick.ExifTag.UserComment) == null)
                                                {
                                                    if (!string.IsNullOrEmpty(comment)) SetAttribute(image, tag, value_old);
                                                }
                                                else comment = GetAttribute(image, tag);
                                            }
                                        }
                                    }
                                    catch (Exception ex) { Log(ex.Message); }
                                }
                            }
                            #endregion
                            #region touch keywords
                            if (meta is MetaInfo && meta.ChangeProperties.HasFlag(ChangePropertyType.Keywords))
                            {
                                foreach (var tag in tag_keywords)
                                {
                                    try
                                    {
                                        var value_old = GetAttribute(image, tag);
                                        if (force || (!image.AttributeNames.Contains(tag) && !string.IsNullOrEmpty(keywords)))
                                        {
                                            SetAttribute(image, tag, keywords);
                                            var value_new = GetAttribute(image, tag);
                                            Log($"{$"{tag}".PadRight(32)}= {(value_old == null ? "NULL" : value_old)} => {value_new}");
                                        }
                                        else
                                        {
                                            if (tag.Equals("exif:WinXP-Keywords"))
                                            {
                                                if (exif.GetValue(ImageMagick.ExifTag.XPKeywords) == null)
                                                {
                                                    if (!string.IsNullOrEmpty(keywords)) SetAttribute(image, tag, value_old);
                                                }
                                                else keywords = GetAttribute(image, tag);
                                            }
                                        }
                                    }
                                    catch (Exception ex) { Log(ex.Message); }
                                }
                            }
                            #endregion
                            #region touch rating
                            if (meta is MetaInfo && (meta.ChangeProperties.HasFlag(ChangePropertyType.Rating) || meta.ChangeProperties.HasFlag(ChangePropertyType.Ranking)))
                            {
                                foreach (var tag in tag_rating)
                                {
                                    try
                                    {
                                        var value_old = GetAttribute(image, tag);
                                        if (force || (!image.AttributeNames.Contains(tag) && rating.HasValue))
                                        {
                                            if (tag.Equals("RatingPercent", StringComparison.CurrentCultureIgnoreCase))
                                                SetAttribute(image, tag, rating);
                                            else if (tag.Equals("Rating", StringComparison.CurrentCultureIgnoreCase))
                                                SetAttribute(image, tag, RatingToRanking(rating));
                                            else if (tag.Equals("xmp:Rating", StringComparison.CurrentCultureIgnoreCase))
                                                SetAttribute(image, tag, RatingToRanking(rating));
                                            else
                                                SetAttribute(image, tag, rating);
                                            var value_new = GetAttribute(image, tag);
                                            Log($"{$"{tag}".PadRight(32)}= {(value_old == null ? "NULL" : value_old)} => {value_new}");
                                        }
                                        else
                                        {
                                            if (tag.Equals("MicrosoftPhoto:Rating"))
                                            {
                                                if (exif.GetValue(ImageMagick.ExifTag.RatingPercent) == null)
                                                {
                                                    if (!string.IsNullOrEmpty(keywords)) SetAttribute(image, tag, value_old);
                                                }
                                                else rating = Convert.ToInt32(GetAttribute(image, tag));
                                            }
                                        }
                                    }
                                    catch (Exception ex) { Log(ex.Message); }
                                }
                            }
                            #endregion
                            #region touch software
                            var tag_software = "Software";
                            if (image.AttributeNames.Contains(tag_software) && !image.AttributeNames.Contains($"exif:{tag_software}"))
                                SetAttribute(image, $"exif:{tag_software}", GetAttribute(image, tag_software));
                            if (!image.AttributeNames.Contains(tag_software) && image.AttributeNames.Contains($"exif:{tag_software}"))
                                SetAttribute(image, tag_software, GetAttribute(image, $"exif:{tag_software}"));
                            if (string.IsNullOrEmpty(GetAttribute(image, $"exif:{tag_software}")) &&
                                string.IsNullOrEmpty(GetAttribute(image, $"{tag_software}")) &&
                                !string.IsNullOrEmpty(GetAttribute(image, $"exif:MakerNote")))
                            {
                                SetAttribute(image, $"exif:{tag_software}", GetAttribute(image, $"exif:MakerNote"));
                                //SetAttribute(image, $"exif:MakerNote", GetAttribute(image, $"exif:MakerNote"));
                            }
                            #endregion
                            Log($"{"Profiles".PadRight(32)}= {string.Join(", ", image.ProfileNames)}");

                            //if (exif != null) image.SetProfile(exif);
                            #region touch xmp profile
                            #region Init a XMP contents
                            if (xmp == null)
                            {
                                //var xml = $"<?xpacket begin='?' id='W5M0MpCehiHzreSzNTczkc9d'?>{Environment.NewLine}<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\"><rdf:Description rdf:about=\"uuid:faf5bdd5-ba3d-11da-ad31-d33d75182f1b\" xmlns:MicrosoftPhoto=\"http://ns.microsoft.com/photo/1.0/\"><MicrosoftPhoto:DateAcquired>{dm_msxmp}</MicrosoftPhoto:DateAcquired></rdf:Description></rdf:RDF></x:xmpmeta>{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}                            <?xpacket end='w'?>";
                                //var xml = $"<?xpacket begin='?' id='W5M0MpCehiHzreSzNTczkc9d'?><x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\"><rdf:Description rdf:about=\"uuid:faf5bdd5-ba3d-11da-ad31-d33d75182f1b\" xmlns:MicrosoftPhoto=\"http://ns.microsoft.com/photo/1.0/\"><MicrosoftPhoto:DateAcquired>{dm_msxmp}</MicrosoftPhoto:DateAcquired><MicrosoftPhoto:DateTaken>{dm_msxmp}</MicrosoftPhoto:DateTaken></rdf:Description><rdf:Description about='' xmlns:exif='http://ns.adobe.com/exif/1.0/'><exif:DateTimeDigitized>{dm_ms}</exif:DateTimeDigitized><exif:DateTimeOriginal>{dm_ms}</exif:DateTimeOriginal></rdf:Description></rdf:RDF></x:xmpmeta><?xpacket end='w'?>";

                                var xml = $"<?xpacket begin='?' id='W5M0MpCehiHzreSzNTczkc9d'?><x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\"></rdf:RDF></x:xmpmeta><?xpacket end='w'?>";

                                if (image.AttributeNames.Contains("exif:ExtensibleMetadataPlatform"))
                                    xml = GetAttribute(image, "exif:ExtensibleMetadataPlatform");
                                else if (image.AttributeNames.Contains("exif:XmpMetadata"))
                                    xml = GetAttribute(image, "exif:XmpMetadata");

                                xmp = new XmpProfile(Encoding.UTF8.GetBytes(xml));
                                image.SetProfile(xmp);
                            }
                            #endregion
                            #region Update xmp nodes
                            if (xmp != null)
                            {
                                var xml = Encoding.UTF8.GetString(xmp.GetData());
                                try
                                {
                                    var xml_doc = new XmlDocument();
                                    xml_doc.LoadXml(xml);
                                    var root_nodes = xml_doc.GetElementsByTagName("rdf:RDF");
                                    if (root_nodes.Count >= 1)
                                    {
                                        var root_node = root_nodes.Item(0);
                                        #region Title/Comment node
                                        if (xml_doc.GetElementsByTagName("dc:title").Count <= 0)
                                        {
                                            var desc = xml_doc.CreateElement("rdf:Description", "rdf");
                                            desc.SetAttribute("xmlns:dc", xmp_ns_lookup["dc"]);
                                            desc.AppendChild(xml_doc.CreateElement("dc:title", "dc"));
                                            root_node.AppendChild(desc);
                                        }
                                        #endregion
                                        #region Comment node
                                        if (xml_doc.GetElementsByTagName("dc:description").Count <= 0)
                                        {
                                            var desc = xml_doc.CreateElement("rdf:Description", "rdf");
                                            desc.SetAttribute("xmlns:dc", xmp_ns_lookup["dc"]);
                                            desc.AppendChild(xml_doc.CreateElement("dc:description", "dc"));
                                            root_node.AppendChild(desc);
                                        }
                                        #endregion
                                        #region Author node
                                        if (xml_doc.GetElementsByTagName("dc:creator").Count <= 0)
                                        {
                                            var desc = xml_doc.CreateElement("rdf:Description", "rdf");
                                            desc.SetAttribute("xmlns:dc", xmp_ns_lookup["dc"]);
                                            desc.AppendChild(xml_doc.CreateElement("dc:creator", "dc"));
                                            root_node.AppendChild(desc);
                                        }
                                        if (xml_doc.GetElementsByTagName("xmp:creator").Count <= 0)
                                        {
                                            var desc = xml_doc.CreateElement("rdf:Description", "rdf");
                                            desc.SetAttribute("xmlns:xmp", xmp_ns_lookup["xmp"]);
                                            desc.AppendChild(xml_doc.CreateElement("xmp:creator", "xmp"));
                                            root_node.AppendChild(desc);
                                        }
                                        #endregion
                                        #region Keywords node
                                        if (xml_doc.GetElementsByTagName("dc:subject").Count <= 0)
                                        {
                                            var desc = xml_doc.CreateElement("rdf:Description", "rdf");
                                            desc.SetAttribute("xmlns:dc", xmp_ns_lookup["dc"]);
                                            desc.AppendChild(xml_doc.CreateElement("dc:subject", "dc"));
                                            root_node.AppendChild(desc);
                                        }
                                        if (xml_doc.GetElementsByTagName("MicrosoftPhoto:LastKeywordXMP").Count <= 0)
                                        {
                                            var desc = xml_doc.CreateElement("rdf:Description", "rdf");
                                            desc.SetAttribute("xmlns:MicrosoftPhoto", xmp_ns_lookup["MicrosoftPhoto"]);
                                            desc.AppendChild(xml_doc.CreateElement("MicrosoftPhoto:LastKeywordXMP", "MicrosoftPhoto"));
                                            root_node.AppendChild(desc);
                                        }
                                        if (xml_doc.GetElementsByTagName("MicrosoftPhoto:LastKeywordIPTC").Count <= 0)
                                        {
                                            var desc = xml_doc.CreateElement("rdf:Description", "rdf");
                                            desc.SetAttribute("xmlns:MicrosoftPhoto", xmp_ns_lookup["MicrosoftPhoto"]);
                                            desc.AppendChild(xml_doc.CreateElement("MicrosoftPhoto:LastKeywordIPTC", "MicrosoftPhoto"));
                                            root_node.AppendChild(desc);
                                        }
                                        if (xml_doc.GetElementsByTagName("MicrosoftPhoto:LastKeywordIPTC_TIFF_IRB").Count <= 0)
                                        {
                                            var desc = xml_doc.CreateElement("rdf:Description", "rdf");
                                            desc.SetAttribute("xmlns:MicrosoftPhoto", xmp_ns_lookup["MicrosoftPhoto"]);
                                            desc.AppendChild(xml_doc.CreateElement("MicrosoftPhoto:LastKeywordIPTC_TIFF_IRB", "MicrosoftPhoto"));
                                            root_node.AppendChild(desc);
                                        }
                                        #endregion
                                        #region Copyright node
                                        if (xml_doc.GetElementsByTagName("dc:rights").Count <= 0)
                                        {
                                            var desc = xml_doc.CreateElement("rdf:Description", "rdf");
                                            desc.SetAttribute("xmlns:dc", xmp_ns_lookup["dc"]);
                                            desc.AppendChild(xml_doc.CreateElement("dc:rights", "dc"));
                                            root_node.AppendChild(desc);
                                        }
                                        #endregion
                                        #region CreateTime node
                                        if (xml_doc.GetElementsByTagName("xmp:CreateDate").Count <= 0)
                                        {
                                            var desc = xml_doc.CreateElement("rdf:Description", "rdf");
                                            desc.SetAttribute("rdf:about", "uuid:faf5bdd5-ba3d-11da-ad31-d33d75182f1b");
                                            desc.SetAttribute("xmlns:xmp", xmp_ns_lookup["xmp"]);
                                            desc.AppendChild(xml_doc.CreateElement("xmp:CreateDate", "xmp"));
                                            root_node.AppendChild(desc);
                                        }
                                        #endregion
                                        #region ModifyDate node
                                        if (xml_doc.GetElementsByTagName("xmp:ModifyDate").Count <= 0)
                                        {
                                            var desc = xml_doc.CreateElement("rdf:Description", "rdf");
                                            desc.SetAttribute("rdf:about", "uuid:faf5bdd5-ba3d-11da-ad31-d33d75182f1b");
                                            desc.SetAttribute("xmlns:xmp", xmp_ns_lookup["xmp"]);
                                            desc.AppendChild(xml_doc.CreateElement("xmp:ModifyDate", "xmp"));
                                            root_node.AppendChild(desc);
                                        }
                                        #endregion
                                        #region DateTimeOriginal node
                                        if (xml_doc.GetElementsByTagName("xmp:DateTimeOriginal").Count <= 0)
                                        {
                                            var desc = xml_doc.CreateElement("rdf:Description", "rdf");
                                            desc.SetAttribute("rdf:about", "uuid:faf5bdd5-ba3d-11da-ad31-d33d75182f1b");
                                            desc.SetAttribute("xmlns:xmp", xmp_ns_lookup["xmp"]);
                                            desc.AppendChild(xml_doc.CreateElement("xmp:DateTimeOriginal", "xmp"));
                                            root_node.AppendChild(desc);
                                        }
                                        #endregion
                                        #region DateTimeDigitized node
                                        if (xml_doc.GetElementsByTagName("xmp:DateTimeDigitized").Count <= 0)
                                        {
                                            var desc = xml_doc.CreateElement("rdf:Description", "rdf");
                                            desc.SetAttribute("rdf:about", "uuid:faf5bdd5-ba3d-11da-ad31-d33d75182f1b");
                                            desc.SetAttribute("xmlns:xmp", xmp_ns_lookup["xmp"]);
                                            desc.AppendChild(xml_doc.CreateElement("xmp:DateTimeDigitized", "xmp"));
                                            root_node.AppendChild(desc);
                                        }
                                        #endregion
                                        #region Ranking/Rating node
                                        if (xml_doc.GetElementsByTagName("xmp:Rating").Count <= 0 && rating > 0)
                                        {
                                            var desc = xml_doc.CreateElement("rdf:Description", "rdf");
                                            desc.SetAttribute("rdf:about", "uuid:faf5bdd5-ba3d-11da-ad31-d33d75182f1b");
                                            desc.SetAttribute("xmlns:xmp", xmp_ns_lookup["xmp"]);
                                            desc.AppendChild(xml_doc.CreateElement("xmp:Rating", "xmp"));
                                            root_node.AppendChild(desc);
                                        }
                                        if (xml_doc.GetElementsByTagName("MicrosoftPhoto:Rating").Count <= 0 && rating > 0)
                                        {
                                            var desc = xml_doc.CreateElement("rdf:Description", "rdf");
                                            desc.SetAttribute("rdf:about", "uuid:faf5bdd5-ba3d-11da-ad31-d33d75182f1b");
                                            desc.SetAttribute("xmlns:MicrosoftPhoto", xmp_ns_lookup["MicrosoftPhoto"]);
                                            desc.AppendChild(xml_doc.CreateElement("MicrosoftPhoto:Rating", "MicrosoftPhoto"));
                                            root_node.AppendChild(desc);
                                        }
                                        #endregion
                                        #region EXIF DateTime node
                                        if (xml_doc.GetElementsByTagName("exif:DateTimeDigitized").Count <= 0)
                                        {
                                            if (xml_doc.GetElementsByTagName("exif:DateTimeOriginal").Count > 0)
                                            {
                                                var node_msdt = xml_doc.GetElementsByTagName("exif:DateTimeOriginal").Item(0);
                                                node_msdt.ParentNode.AppendChild(xml_doc.CreateElement("exif:DateTimeDigitized", "exif"));
                                            }
                                            else
                                            {
                                                var desc = xml_doc.CreateElement("rdf:Description", "rdf");
                                                desc.SetAttribute("rdf:about", "");
                                                desc.SetAttribute("xmlns:exif", xmp_ns_lookup["exif"]);
                                                desc.AppendChild(xml_doc.CreateElement("exif:DateTimeDigitized", "exif"));
                                                root_node.AppendChild(desc);
                                            }
                                        }
                                        if (xml_doc.GetElementsByTagName("exif:DateTimeOriginal").Count <= 0)
                                        {
                                            if (xml_doc.GetElementsByTagName("exif:DateTimeDigitized").Count > 0)
                                            {
                                                var node_msdt = xml_doc.GetElementsByTagName("exif:DateTimeDigitized").Item(0);
                                                node_msdt.ParentNode.AppendChild(xml_doc.CreateElement("exif:DateTimeOriginal", "exif"));
                                            }
                                            else
                                            {
                                                var desc = xml_doc.CreateElement("rdf:Description", "rdf");
                                                desc.SetAttribute("rdf:about", "");
                                                desc.SetAttribute("xmlns:exif", xmp_ns_lookup["exif"]);
                                                desc.AppendChild(xml_doc.CreateElement("exif:DateTimeOriginal", "exif"));
                                                root_node.AppendChild(desc);
                                            }
                                        }
                                        #endregion
                                        #region TIFF DateTime node
                                        if (xml_doc.GetElementsByTagName("tiff:DateTime").Count <= 0)
                                        {
                                            var desc = xml_doc.CreateElement("rdf:Description", "rdf");
                                            desc.SetAttribute("rdf:about", "");
                                            desc.SetAttribute("xmlns:tiff", xmp_ns_lookup["tiff"]);
                                            desc.AppendChild(xml_doc.CreateElement("tiff:DateTime", "tiff"));
                                            root_node.AppendChild(desc);
                                        }
                                        #endregion
                                        #region MicrosoftPhoto DateTime node
                                        if (xml_doc.GetElementsByTagName("MicrosoftPhoto:DateAcquired").Count <= 0)
                                        {
                                            if (xml_doc.GetElementsByTagName("MicrosoftPhoto:DateTaken").Count > 0)
                                            {
                                                var node_msdt = xml_doc.GetElementsByTagName("MicrosoftPhoto:DateTaken").Item(0);
                                                node_msdt.ParentNode.AppendChild(xml_doc.CreateElement("MicrosoftPhoto:DateAcquired", "MicrosoftPhoto"));
                                            }
                                            else
                                            {
                                                var desc = xml_doc.CreateElement("rdf:Description", "rdf");
                                                desc.SetAttribute("rdf:about", "uuid:faf5bdd5-ba3d-11da-ad31-d33d75182f1b");
                                                desc.SetAttribute("xmlns:MicrosoftPhoto", xmp_ns_lookup["MicrosoftPhoto"]);
                                                desc.AppendChild(xml_doc.CreateElement("MicrosoftPhoto:DateAcquired", "MicrosoftPhoto"));
                                                root_node.AppendChild(desc);
                                            }
                                        }
                                        if (xml_doc.GetElementsByTagName("MicrosoftPhoto:DateTaken").Count <= 0)
                                        {
                                            if (xml_doc.GetElementsByTagName("MicrosoftPhoto:DateAcquired").Count > 0)
                                            {
                                                var node_msdt = xml_doc.GetElementsByTagName("MicrosoftPhoto:DateAcquired").Item(0);
                                                node_msdt.ParentNode.AppendChild(xml_doc.CreateElement("MicrosoftPhoto:DateTaken", "MicrosoftPhoto"));
                                            }
                                            else
                                            {
                                                var desc = xml_doc.CreateElement("rdf:Description", "rdf");
                                                desc.SetAttribute("rdf:about", "uuid:faf5bdd5-ba3d-11da-ad31-d33d75182f1b");
                                                desc.SetAttribute("xmlns:MicrosoftPhoto", xmp_ns_lookup["MicrosoftPhoto"]);
                                                desc.AppendChild(xml_doc.CreateElement("MicrosoftPhoto:DateTaken", "MicrosoftPhoto"));
                                                root_node.AppendChild(desc);
                                            }
                                        }
                                        #endregion
                                        #region Remove duplicate node
                                        var all_elements = new List<string>()
                                        {
                                            "dc:title",
                                            "dc:description",
                                            "dc:creator", "xmp:creator",
                                            "dc:subject", "MicrosoftPhoto:LastKeywordXMP", "MicrosoftPhoto:LastKeywordIPTC", "MicrosoftPhoto:LastKeywordIPTC_TIFF_IRB",
                                            "dc:rights",
                                            "xmp:CreateDate", "xmp:ModifyDate", "xmp:DateTimeOriginal", "xmp:DateTimeDigitized",
                                            "xmp:Rating", "MicrosoftPhoto:Rating",
                                            "exif:DateTimeDigitized", "exif:DateTimeOriginal",
                                            "tiff:DateTime",
                                            "MicrosoftPhoto:DateAcquired", "MicrosoftPhoto:DateTaken",
                                        };
                                        all_elements.AddRange(tag_author);
                                        all_elements.AddRange(tag_comments);
                                        all_elements.AddRange(tag_copyright);
                                        all_elements.AddRange(tag_date);
                                        all_elements.AddRange(tag_keywords);
                                        all_elements.AddRange(tag_rating);
                                        all_elements.AddRange(tag_subject);
                                        all_elements.AddRange(tag_title);
                                        all_elements.Add(tag_software);
                                        foreach (var element in all_elements.Distinct())
                                        {
                                            var nodes = xml_doc.GetElementsByTagName(element);
                                            if (nodes.Count > 1)
                                            {
                                                for (var i = 1; i < nodes.Count; i++)
                                                {
                                                    nodes[i].ParentNode.RemoveChild(nodes[i]);
                                                }
                                            }
                                        }
                                        #endregion

                                        #region xml nodes updating
                                        var rdf_attr = "xmlns:rdf";
                                        var rdf_value = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
                                        Action<XmlElement, dynamic> add_rdf_li = new Action<XmlElement, dynamic>((element, text)=>
                                        {
                                            if (text is string && !string.IsNullOrEmpty(text as string))
                                            {
                                                var items = (text as string).Split(new string[] { ";", "#" }, StringSplitOptions.RemoveEmptyEntries).Select(k => k.Trim()).Where(k => !string.IsNullOrEmpty(k)).Distinct();
                                                foreach (var item in items)
                                                {
                                                    var node_author_li = xml_doc.CreateElement("rdf:li", "rdf");
                                                    node_author_li.InnerText = item;
                                                    element.AppendChild(node_author_li);
                                                }
                                            }
                                            else if(text is IEnumerable<string> && (text as IEnumerable<string>).Count() > 0)
                                            {
                                                foreach (var item in (text as IEnumerable<string>))
                                                {
                                                    var node_author_li = xml_doc.CreateElement("rdf:li", "rdf");
                                                    node_author_li.InnerText = item;
                                                    element.AppendChild(node_author_li);
                                                }
                                            }
                                        });
                                        foreach (XmlNode node in xml_doc.GetElementsByTagName("rdf:Description"))
                                        {
                                            var nodes = new List<XmlNode>();
                                            foreach (XmlNode child in node.ChildNodes)
                                            {
                                                if (child.Name.Equals("dc:title", StringComparison.CurrentCultureIgnoreCase))
                                                {
                                                    child.RemoveAll();
                                                    var node_title = xml_doc.CreateElement("rdf:Alt", "rdf");
                                                    var node_title_li = xml_doc.CreateElement("rdf:li", "rdf");
                                                    node_title_li.SetAttribute("xml:lang", "x-default");
                                                    node_title_li.InnerText = title;
                                                    node_title.AppendChild(node_title_li);
                                                    child.AppendChild(node_title);
                                                }
                                                else if (child.Name.Equals("dc:description", StringComparison.CurrentCultureIgnoreCase))
                                                {
                                                    child.RemoveAll();
                                                    var node_comment = xml_doc.CreateElement("rdf:Alt", "rdf");
                                                    var node_comment_li = xml_doc.CreateElement("rdf:li", "rdf");
                                                    node_comment_li.SetAttribute("xml:lang", "x-default");
                                                    node_comment_li.InnerText = comment;
                                                    node_comment.AppendChild(node_comment_li);
                                                    child.AppendChild(node_comment);
                                                }
                                                else if (child.Name.Equals("xmp:creator", StringComparison.CurrentCultureIgnoreCase) ||
                                                    child.Name.Equals("dc:creator", StringComparison.CurrentCultureIgnoreCase))
                                                {
                                                    child.RemoveAll();
                                                    var node_author = xml_doc.CreateElement("rdf:Seq", "rdf");
                                                    node_author.SetAttribute(rdf_attr, rdf_value);
                                                    add_rdf_li.Invoke(node_author, authors);
                                                    child.AppendChild(node_author);
                                                }
                                                else if (child.Name.Equals("dc:rights", StringComparison.CurrentCultureIgnoreCase))
                                                {
                                                    child.RemoveAll();
                                                    var node_rights = xml_doc.CreateElement("rdf:Bag", "rdf");
                                                    node_rights.SetAttribute(rdf_attr, rdf_value);
                                                    add_rdf_li.Invoke(node_rights, copyrights);
                                                    child.AppendChild(node_rights);
                                                }
                                                else if (child.Name.Equals("dc:subject", StringComparison.CurrentCultureIgnoreCase) ||
                                                    child.Name.StartsWith("MicrosoftPhoto:LastKeyword", StringComparison.CurrentCultureIgnoreCase))
                                                {
                                                    child.RemoveAll();
                                                    var node_subject = xml_doc.CreateElement("rdf:Bag", "rdf");
                                                    node_subject.SetAttribute(rdf_attr, rdf_value);
                                                    add_rdf_li.Invoke(node_subject, keyword_list);
                                                    child.AppendChild(node_subject);
                                                }
                                                else if (child.Name.Equals("MicrosoftPhoto:Rating", StringComparison.CurrentCultureIgnoreCase))
                                                {
                                                    child.InnerText = $"{rating}";
                                                    //if (rating > 0) child.InnerText = $"{rating}";
                                                    //else child.ParentNode.RemoveChild(child);
                                                }
                                                else if (child.Name.Equals("xmp:Rating", StringComparison.CurrentCultureIgnoreCase))
                                                {
                                                    var rating_level = RatingToRanking(rating);
                                                    var rating_value = rating_level <= 0 ? string.Empty : $"{rating_level}";
                                                    child.InnerText = $"{rating_value}";
                                                    //if (rating_level > 0) child.InnerText = $"{rating_level}";
                                                    //else child.ParentNode.RemoveChild(child);
                                                }
                                                else if (child.Name.Equals("xmp:CreateDate", StringComparison.CurrentCultureIgnoreCase))
                                                    child.InnerText = dc_xmp;
                                                else if (child.Name.Equals("xmp:ModifyDate", StringComparison.CurrentCultureIgnoreCase))
                                                    child.InnerText = dm_xmp;
                                                else if (child.Name.Equals("xmp:DateTimeOriginal", StringComparison.CurrentCultureIgnoreCase))
                                                    child.InnerText = dm_date;
                                                else if (child.Name.Equals("xmp:DateTimeDigitized", StringComparison.CurrentCultureIgnoreCase))
                                                    child.InnerText = dm_date;
                                                else if (child.Name.Equals("MicrosoftPhoto:DateAcquired", StringComparison.CurrentCultureIgnoreCase))
                                                    child.InnerText = dm_msxmp;
                                                else if (child.Name.Equals("MicrosoftPhoto:DateTaken", StringComparison.CurrentCultureIgnoreCase))
                                                    child.InnerText = dm_msxmp;
                                                else if (child.Name.Equals("exif:DateTimeDigitized", StringComparison.CurrentCultureIgnoreCase))
                                                    child.InnerText = dm_ms;
                                                else if (child.Name.Equals("exif:DateTimeOriginal", StringComparison.CurrentCultureIgnoreCase))
                                                    child.InnerText = dm_ms;
                                                else if (child.Name.Equals("tiff:DateTime", StringComparison.CurrentCultureIgnoreCase))
                                                    child.InnerText = dm_ms;

                                                if (tag_date.Contains(node.Name, StringComparer.CurrentCultureIgnoreCase))
                                                {
                                                    if (nodes.Count(n => n.Name.Equals(child.Name, StringComparison.CurrentCultureIgnoreCase)) > 0) node.RemoveChild(child);
                                                    else nodes.Add(child);
                                                }
                                            }
                                            nodes.Clear();
                                        }
                                        #endregion
                                        #region pretty xml
                                        xml = FormatXML(xml_doc, true);
                                        #endregion
                                    }
                                }
#if DEBUG
                                catch (Exception ex)
                                {
                                    Log($"{ex.Message}{ex.StackTrace}");
#else
                                catch
                                {
#endif
                                    #region Title
                                    var pattern_title = @"(<dc:title>.*?<rdf:li.*?xml:lang='.*?')(>).*?(</rdf:li></rdf:Alt></dc:title>)";
                                    if (Regex.IsMatch(xml, pattern_title, RegexOptions.IgnoreCase | RegexOptions.Multiline))
                                    {
                                        xml = Regex.Replace(xml, pattern_title, $"$1$2{title}$3", RegexOptions.IgnoreCase | RegexOptions.Multiline);
                                        xml = xml.Replace("$2", ">");
                                    }
                                    else
                                    {
                                        var title_xml = $"<rdf:Description rdf:about='' xmlns:dc='http://purl.org/dc/elements/1.1/'><dc:title><rdf:Alt><rdf:li xml:lang='x-default'>{title}</rdf:li></rdf:Alt></dc:title></rdf:Description>";
                                        xml = Regex.Replace(xml, @"(</rdf:RDF>.*?</x:xmpmeta>)", $"{title_xml}$1", RegexOptions.IgnoreCase);
                                    }
                                    #endregion
                                    #region MS Photo DateAcquired
                                    var pattern_ms_da = @"(<MicrosoftPhoto:DateAcquired>).*?(</MicrosoftPhoto:DateAcquired>)";
                                    if (Regex.IsMatch(xml, pattern_ms_da, RegexOptions.IgnoreCase))
                                    {
                                        xml = Regex.Replace(xml, pattern_ms_da, $"$1{dm_msxmp}$2", RegexOptions.IgnoreCase);
                                        xml = xml.Replace("$1", "<MicrosoftPhoto:DateAcquired>");
                                    }
                                    else
                                    {
                                        var msda_xml = $"<rdf:Description rdf:about=\"uuid:faf5bdd5-ba3d-11da-ad31-d33d75182f1b\" xmlns:MicrosoftPhoto=\"http://ns.microsoft.com/photo/1.0/\"><MicrosoftPhoto:DateAcquired>{dm_msxmp}</MicrosoftPhoto:DateAcquired></rdf:Description>";
                                        xml = Regex.Replace(xml, @"(</rdf:RDF></x:xmpmeta>)", $"{msda_xml}$1", RegexOptions.IgnoreCase);
                                    }
                                    #endregion
                                    #region MS Photo DateTaken
                                    var pattern_ms_dt = @"(<MicrosoftPhoto:DateTaken>).*?(</MicrosoftPhoto:DateTaken>)";
                                    if (Regex.IsMatch(xml, pattern_ms_dt, RegexOptions.IgnoreCase))
                                    {
                                        xml = Regex.Replace(xml, pattern_ms_dt, $"$1{dm_msxmp}$2", RegexOptions.IgnoreCase);
                                        xml = xml.Replace("$1", "<MicrosoftPhoto:DateTaken>");
                                    }
                                    else
                                    {
                                        var msdt_xml = $"<rdf:Description rdf:about=\"uuid:faf5bdd5-ba3d-11da-ad31-d33d75182f1b\" xmlns:MicrosoftPhoto=\"http://ns.microsoft.com/photo/1.0/\"><MicrosoftPhoto:DateTaken>{dm_msxmp}</MicrosoftPhoto:DateTaken></rdf:Description>";
                                        xml = Regex.Replace(xml, @"(</rdf:RDF></x:xmpmeta>)", $"{msdt_xml}$1", RegexOptions.IgnoreCase);
                                    }
                                    #endregion
                                    #region tiff:DateTime
                                    var pattern_tiff_dt = @"(<tiff:DateTime>).*?(</tiff:DateTime>)";
                                    if (Regex.IsMatch(xml, pattern_tiff_dt, RegexOptions.IgnoreCase))
                                    {
                                        xml = Regex.Replace(xml, pattern_tiff_dt, $"$1{dm_ms}$2", RegexOptions.IgnoreCase);
                                        xml = xml.Replace("$1", "<tiff:DateTime>");
                                    }
                                    else
                                    {
                                        var tiffdt_xml = $"<rdf:Description rdf:about='' xmlns:tiff='http://ns.adobe.com/tiff/1.0/'><tiff:DateTime>{dm_ms}</tiff:DateTime></rdf:Description>";
                                        xml = Regex.Replace(xml, @"(</rdf:RDF></x:xmpmeta>)", $"{tiffdt_xml}$1", RegexOptions.IgnoreCase);
                                    }
                                    #endregion
                                    #region exif:DateTimeDigitized
                                    var pattern_exif_dd = @"(<exif:DateTimeDigitized>).*?(</exif:DateTimeDigitized>)";
                                    if (Regex.IsMatch(xml, pattern_exif_dd, RegexOptions.IgnoreCase))
                                    {
                                        xml = Regex.Replace(xml, pattern_exif_dd, $"$1{dm_ms}$2", RegexOptions.IgnoreCase);
                                        xml = xml.Replace("$1", "<exif:DateTimeDigitized>");
                                    }
                                    else
                                    {
                                        var exifdo_xml = $"<rdf:Description rdf:about='' xmlns:exif='http://ns.adobe.com/exif/1.0/'><exif:DateTimeDigitized>{dm_ms}</exif:DateTimeDigitized></rdf:Description>";
                                        xml = Regex.Replace(xml, @"(</rdf:RDF></x:xmpmeta>)", $"{exifdo_xml}$1", RegexOptions.IgnoreCase);
                                    }
                                    #endregion
                                    #region exif:DateTimeOriginal
                                    var pattern_exif_do = @"(<exif:DateTimeOriginal>).*?(</exif:DateTimeOriginal>)";
                                    if (Regex.IsMatch(xml, pattern_exif_do, RegexOptions.IgnoreCase))
                                    {
                                        xml = Regex.Replace(xml, pattern_exif_do, $"$1{dm_ms}$2", RegexOptions.IgnoreCase);
                                        xml = xml.Replace("$1", "<exif:DateTimeOriginal>");
                                    }
                                    else
                                    {
                                        var exifdo_xml = $"<rdf:Description rdf:about='' xmlns:exif='http://ns.adobe.com/exif/1.0/'><exif:DateTimeOriginal>{dm_ms}</exif:DateTimeOriginal></rdf:Description>";
                                        xml = Regex.Replace(xml, @"(</rdf:RDF></x:xmpmeta>)", $"{exifdo_xml}$1", RegexOptions.IgnoreCase);
                                    }
                                    #endregion
                                }

                                exif = image.HasProfile("exif") ? image.GetExifProfile() : new ExifProfile();
                                exif.SetValue<byte[]>(ImageMagick.ExifTag.XMP, Encoding.UTF8.GetBytes(xml));
                                if (exif is ExifProfile) image.SetProfile(exif);
#if DEBUG
                                //var x = Encoding.UTF8.GetString(exif.GetValue<byte[]>(ImageMagick.ExifTag.XMP).Value);
#endif
                                xmp = new XmpProfile(Encoding.UTF8.GetBytes(xml));
                                if (xmp is XmpProfile) image.SetProfile(xmp);
                                if (meta is MetaInfo && meta.ShowXMP) Log($"{"XMP Profiles".PadRight(32)}= {xml}");
                            }
                            #endregion
                            #endregion

                            #region save touched image
                            FixDPI(image);
#if DEBUG
                            image.Endian = Endian.Undefined;
                            using (var ms = new MemoryStream())
                            {
                                image.Write(ms, image.Format);
                                File.WriteAllBytes(fi.FullName, ms.ToArray());
                            }
#else
                            image.Write(fi.FullName, image.Format);
#endif

                            var exifdata_n = new ExifData(fi.FullName);
                            if (exifdata.ByteOrder != exifdata_n.ByteOrder)
                            {
                                if (image.Endian == Endian.Undefined) image.Endian = exifdata_n.ByteOrder == ExifByteOrder.BigEndian ? Endian.MSB : Endian.LSB;
                                SetAttribute(image, "exif:UserComment", comment);
                                SetAttribute(image, "Rating", RatingToRanking(rating));
                                SetAttribute(image, "RatingPercent", rating);
                                image.Write(fi.FullName, image.Format);
                            }
                            #endregion

                            fi.CreationTime = dc;
                            fi.LastWriteTime = dm;
                            fi.LastAccessTime = da;

                            #region touch PNG image with CompactExifLib
                            if (is_png)
                            {
                                var meta_new = new MetaInfo()
                                {
                                    DateAccesed = da,
                                    DateCreated = dc,
                                    DateModified = dm,

                                    DateAcquired = dm,
                                    DateTaken = dm,

                                    Title = title,
                                    Subject = subject,
                                    Copyrights = copyrights,
                                    Authors = authors,
                                    Keywords = keywords,
                                    Comment = comment,

                                    RatingPercent = rating,
                                    Rating = RatingToRanking(rating),
                                };

                                TouchMetaAlt(file, meta: meta_new);
                            }
                            #endregion
                        }
                        else
                        {
                            if (!image.FormatInfo.IsReadable)
                                Log($"File \"{file}\" is not a read supported format!");
                            if (!image.FormatInfo.IsWritable)
                                Log($"File \"{file}\" is not a write supported format!");
                        }
                    }
                }
                catch (Exception ex) { Log(ex.Message); }
            }
            else Log($"File \"{file}\" not exists!");
        }

        public static void TouchDate(string file, string dt = null, bool force = false, DateTime? dtc = null, DateTime? dtm = null, DateTime? dta = null, MetaInfo meta = null)
        {
            if (File.Exists(file))
            {
                var fi = new FileInfo(file);
                var dc = dtc ?? (meta is MetaInfo ? meta.DateCreated : null) ?? fi.CreationTime;
                var dm = dtm ?? (meta is MetaInfo ? meta.DateModified : null) ?? fi.LastWriteTime;
                var da = dta ?? (meta is MetaInfo ? meta.DateAccesed : null) ?? fi.LastAccessTime;

                var ov = fi.LastWriteTime.ToString("yyyy-MM-ddTHH:mm:sszzz");

                if (force)
                {
                    if (fi.CreationTime != dc) fi.CreationTime = dc;
                    if (fi.LastWriteTime != dm) fi.LastWriteTime = dm;
                    if (fi.LastAccessTime != da) fi.LastAccessTime = da;
                    Log($"Touching Date From {ov} To {dm.ToString("yyyy-MM-ddTHH:mm:sszzz")}");
                }
                else if (string.IsNullOrEmpty(dt))
                {
                    try
                    {
                        if (SupportedFormats is List<string> && SupportedFormats.Contains(fi.Extension.ToLower())) dm = GetMetaTime(file) ?? dm;
                        fi.CreationTime = dm;
                        fi.LastWriteTime = dm;
                        fi.LastAccessTime = dm;
                        Log($"Touching Date From {ov} To {dm.ToString("yyyy-MM-ddTHH:mm:sszzz")}");
                    }
                    catch (Exception ex)
                    {
#if DEBUG
                        Log($"{ex.Message}{Environment.NewLine}{ex.StackTrace}");
#else
                        Log(ex.Message);
#endif
                    }
                }
                else
                {
                    try
                    {
                        var t = DateTime.Now;
                        if (DateTime.TryParse(dt, out t))
                        {
                            fi.CreationTime = t;
                            fi.LastWriteTime = t;
                            fi.LastAccessTime = t;
                            Log($"Touching Date From {ov} To {t.ToString("yyyy-MM-ddTHH:mm:sszzz")}");
                        }
                    }
                    catch (Exception ex) { Log($"{ex.Message}{Environment.NewLine}{ex.StackTrace}"); }
                }
            }
            else Log($"File \"{file}\" not exists!");
        }

        public static void TouchFolder(string folder, string dt = null, bool force = false, DateTime? dtc = null, DateTime? dtm = null, DateTime? dta = null, MetaInfo meta = null)
        {
            if (Directory.Exists(folder))
            {
                var di = new DirectoryInfo(folder);
                var dc = dtc ?? (meta is MetaInfo ? meta.DateCreated : null) ?? di.CreationTime;
                var dm = dtm ?? (meta is MetaInfo ? meta.DateModified : null) ?? di.LastWriteTime;
                var da = dta ?? (meta is MetaInfo ? meta.DateAccesed : null) ?? di.LastAccessTime;

                var ov = di.LastWriteTime.ToString("yyyy-MM-ddTHH:mm:sszzz");

                if (force)
                {
                    if (di.CreationTime != dc) di.CreationTime = dc;
                    if (di.LastWriteTime != dm) di.LastWriteTime = dm;
                    if (di.LastAccessTime != da) di.LastAccessTime = da;
                    Log($"Touching Date From {ov} To {dm.ToString("yyyy-MM-ddTHH:mm:sszzz")}");
                }
                else if (string.IsNullOrEmpty(dt))
                {
                    try
                    {
                        if (di.CreationTime > dm) di.CreationTime = dm;
                        if (di.LastWriteTime > dm) di.LastWriteTime = dm;
                        if (di.LastAccessTime > dm) di.LastAccessTime = dm;
                        //if (di.CreationTime != dc) Directory.SetCreationTime(folder, dm);
                        //if (di.LastWriteTime != dm) Directory.SetLastAccessTime(folder, dm);
                        //if (di.LastAccessTime != da) Directory.SetLastAccessTime(folder, dm);
                        Log($"Touching Date From {ov} To {dm.ToString("yyyy-MM-ddTHH:mm:sszzz")}");
                    }
                    catch (Exception ex)
                    {
#if DEBUG
                        Log($"{ex.Message}{Environment.NewLine}{ex.StackTrace}");
#else
                        Log(ex.Message);
#endif
                    }
                }
                else
                {
                    try
                    {
                        var t = DateTime.Now;
                        if (DateTime.TryParse(dt, out t))
                        {
                            if (di.CreationTime > t) di.CreationTime = t;
                            if (di.LastWriteTime > t) di.LastWriteTime = t;
                            if (di.LastAccessTime > t) di.LastAccessTime = t;
                            Log($"Touching Date From {ov} To {t.ToString("yyyy-MM-ddTHH:mm:sszzz")}");
                        }
                    }
                    catch (Exception ex) { Log($"{ex.Message}{Environment.NewLine}{ex.StackTrace}"); }
                }
            }
            else Log($"File \"{folder}\" not exists!");
        }

        public static void TouchMetaDate(MagickImage image, FileInfo fi = null, DateTime? dt = null)
        {
            if (image is MagickImage)
            {
                foreach (var tag in tag_date)
                {
                    if (tag.StartsWith("exif"))
                        //image.SetAttribute(tag, dt.Value.ToString("yyyy:MM:dd HH:mm:ss"));
                        SetAttribute(image, tag, dt.Value.ToString("yyyy:MM:dd HH:mm:ss"));
                    else if (tag.StartsWith("MicrosoftPhoto"))
                        image.SetAttribute(tag, dt.Value.ToString("yyyy-MM-ddTHH:mm:ss.fff"));
                    else
                        image.SetAttribute(tag, dt.Value.ToString("yyyy:MM:dd HH:mm:sszzz"));
                }
                var meta_new = GetMetaInfo(image);
                var xml = image.HasProfile("xmp") ? Encoding.UTF8.GetString(image.GetXmpProfile().GetData()) : string.Empty;
                xml = TouchXMP(xml, fi, meta_new);
                image.SetProfile(new XmpProfile(Encoding.UTF8.GetBytes(xml)));
            }
        }

        public static void TouchMetaDate(string file, DateTime? dt = null)
        {
            ///
            /// Test Codes for MagicK.Net will change image byte-order after write image,
            /// if not set profile, image maybe will not change endian, but after set profile,
            /// like exif profile, it will change endian to system endian, windows will to lsb
            ///
            try
            {
                if (dt.HasValue)
                {
                    var fi = new FileInfo(file);
                    using (var image = new MagickImage(fi.FullName))
                    {
                        var dt_old = GetMetaTime(image);
                        TouchMetaDate(image, fi, dt);
                        image.Write(fi.FullName, image.Format);
                        fi.CreationTime = dt.Value;
                        fi.LastWriteTime = dt.Value;
                        fi.LastAccessTime = dt.Value;
                        Log($"Touching Metadata Time From {(dt_old.HasValue ? dt_old.Value.ToString("yyyy-MM-ddTHH:mm:sszzz") : "NULL")} To {(dt.HasValue ? dt.Value.ToString("yyyy-MM-ddTHH:mm:sszzz") : "NULL")}");
                    }
                }
            }
            catch (Exception ex) { Log(ex.Message); }
        }

        public static Stream TouchMetaDate(Stream src, FileInfo fi, DateTime? dt = null)
        {
            ///
            /// Test Codes for MagicK.Net will change image byte-order after write image,
            /// if not set profile, image maybe will not change endian, but after set profile,
            /// like exif profile, it will change endian to system endian, windows will to lsb
            ///
            Stream result = null;
            try
            {
                if (dt.HasValue && src is Stream && src.CanRead)
                {
                    using (var image = new MagickImage(src))
                    {
                        TouchMetaDate(image, fi, dt);
                        result = new MemoryStream();
                        image.Write(result, image.Format);
                    }
                }
            }
            catch (Exception ex) { Log(ex.Message); }
            return (result);
        }

        public static void TouchMetaAlt(string file, bool force = false, DateTime? dtc = null, DateTime? dtm = null, DateTime? dta = null, MetaInfo meta = null, bool pngcs = false)
        {
            try
            {
                if (File.Exists(file))
                {
                    var fi = new FileInfo(file);
                    var dc = dtc ?? (meta is MetaInfo ? meta.DateAcquired ?? meta.DateTaken ?? meta.DateCreated : null) ?? fi.CreationTime;
                    var dm = dtm ?? (meta is MetaInfo ? meta.DateAcquired ?? meta.DateTaken ?? meta.DateModified : null) ?? fi.LastWriteTime;
                    var da = dta ?? (meta is MetaInfo ? meta.DateAcquired ?? meta.DateTaken ?? meta.DateAccesed : null) ?? fi.LastAccessTime;

                    var exif = new ExifData(file);
                    if (exif is ExifData)
                    {
                        if (pngcs) UpdatePngMetaInfo(fi, dm, meta);

                        #region CompactExifLib Update EXIF Metadata
                        DateTime date = dm;
                        if (!force && exif.GetTagValue(CompactExifLib.ExifTag.DateTimeOriginal, out date))
                        {
                            if (date.Ticks != dm.Ticks) exif.SetDateTaken(dm);
                        }
                        else exif.SetDateTaken(dm);
                        if (!force && exif.GetTagValue(CompactExifLib.ExifTag.DateTimeDigitized, out date))
                        {
                            if (date.Ticks != dm.Ticks) exif.SetDateDigitized(dm);
                        }
                        else exif.SetDateDigitized(dm);
                        if (!force && exif.GetTagValue(CompactExifLib.ExifTag.DateTime, out date))
                        {
                            if (date.Ticks != dm.Ticks) exif.SetDateChanged(dm);
                        }
                        else exif.SetDateChanged(dm);

                        if (string.IsNullOrEmpty(meta.Title))
                        {
                            exif.RemoveTag(CompactExifLib.ExifTag.XpTitle);
                            exif.RemoveTag(CompactExifLib.ExifTag.ImageDescription);
                        }
                        else
                        {
                            exif.SetTagRawData(CompactExifLib.ExifTag.XpTitle, ExifTagType.Byte, Encoding.Unicode.GetByteCount(meta.Title), Encoding.Unicode.GetBytes(meta.Title));
                            exif.SetTagValue(CompactExifLib.ExifTag.ImageDescription, meta.Title, StrCoding.Utf8);
                        }

                        if (string.IsNullOrEmpty(meta.Subject))
                        {
                            exif.RemoveTag(CompactExifLib.ExifTag.XpSubject);
                        }
                        else
                        {
                            exif.SetTagRawData(CompactExifLib.ExifTag.XpSubject, ExifTagType.Byte, Encoding.Unicode.GetByteCount(meta.Subject), Encoding.Unicode.GetBytes(meta.Subject));
                        }

                        if (string.IsNullOrEmpty(meta.Keywords))
                        {
                            exif.RemoveTag(CompactExifLib.ExifTag.XpKeywords);
                        }
                        else
                        {
                            exif.SetTagRawData(CompactExifLib.ExifTag.XpKeywords, ExifTagType.Byte, Encoding.Unicode.GetByteCount(meta.Keywords), Encoding.Unicode.GetBytes(meta.Keywords));
                        }

                        if (string.IsNullOrEmpty(meta.Authors))
                        {
                            exif.RemoveTag(CompactExifLib.ExifTag.XpAuthor);
                            exif.RemoveTag(CompactExifLib.ExifTag.Artist);
                        }
                        else
                        {
                            exif.SetTagRawData(CompactExifLib.ExifTag.XpAuthor, ExifTagType.Byte, Encoding.Unicode.GetByteCount(meta.Authors), Encoding.Unicode.GetBytes(meta.Authors));
                            exif.SetTagValue(CompactExifLib.ExifTag.Artist, meta.Authors, StrCoding.Utf8);
                        }

                        if (string.IsNullOrEmpty(meta.Copyrights))
                        {
                            exif.RemoveTag(CompactExifLib.ExifTag.Copyright);
                        }
                        else
                        {
                            exif.SetTagValue(CompactExifLib.ExifTag.Copyright, meta.Copyrights, StrCoding.Utf8);
                        }

                        if (string.IsNullOrEmpty(meta.Comment))
                        {
                            exif.RemoveTag(CompactExifLib.ExifTag.XpComment);
                            exif.RemoveTag(CompactExifLib.ExifTag.UserComment);
                        }
                        else
                        {
                            exif.SetTagRawData(CompactExifLib.ExifTag.XpComment, ExifTagType.Byte, Encoding.Unicode.GetByteCount(meta.Comment), Encoding.Unicode.GetBytes(meta.Comment));
                            exif.SetTagValue(CompactExifLib.ExifTag.UserComment, meta.Comment, StrCoding.IdCode_Utf16);
                        }

                        if (exif.TagExists(CompactExifLib.ExifTag.MakerNote) && !exif.TagExists(CompactExifLib.ExifTag.Software))
                        {
                            ExifTagType type;
                            int count;
                            byte[] value;
                            string note = string.Empty;
                            //if (exif.GetTagValue(CompactExifLib.ExifTag.MakerNote, out note, StrCoding.Utf16Le_Byte))
                            if (exif.GetTagRawData(CompactExifLib.ExifTag.MakerNote, out type, out count, out value))
                            {
                                if (type == ExifTagType.Ascii || type == ExifTagType.Byte || type == ExifTagType.SByte)
                                    note = Encoding.UTF8.GetString(value);
                                else if (type == ExifTagType.Undefined && value is byte[] && value.Length > 0)
                                {
                                    try
                                    {
                                        if (value.Length >= 2 && value[1] == 0x00)
                                            note = Encoding.Unicode.GetString(value);
                                        else
                                            note = Encoding.UTF8.GetString(value);
                                    }
                                    catch { note = Encoding.UTF8.GetString(value.Where(c => c != 0x00).ToArray()); }                                    
                                }
                                if (!string.IsNullOrEmpty(note)) exif.SetTagValue(CompactExifLib.ExifTag.Software, note, StrCoding.Utf8);
                            }
                        }

                        exif.SetTagValue(CompactExifLib.ExifTag.Rating, meta.Rating ?? 0, TagType: ExifTagType.UShort);
                        exif.SetTagValue(CompactExifLib.ExifTag.RatingPercent, meta.RatingPercent ?? 0, TagType: ExifTagType.UShort);
                        #endregion

                        #region CompactExifLib Update XMP RAW data, not profile
                        var xmp = string.Empty;
                        exif.GetTagValue(CompactExifLib.ExifTag.XmpMetadata, out xmp, StrCoding.Utf8);
                        if (string.IsNullOrEmpty(xmp))
                        {
                            ExifTagType type;
                            int bytecount;
                            byte[] bytes;
                            exif.GetTagRawData(CompactExifLib.ExifTag.XmpMetadata, out type, out bytecount, out bytes);
                            if (bytes is byte[] && bytecount > 0) xmp = Encoding.UTF8.GetString(bytes);
                        }
                        xmp = TouchXMP(xmp, fi, meta);
                        //exif.SetTagValue(CompactExifLib.ExifTag.XmpMetadata, xmp, StrCoding.Utf8);
                        exif.SetTagRawData(CompactExifLib.ExifTag.XmpMetadata, ExifTagType.Byte, Encoding.UTF8.GetByteCount(xmp), Encoding.UTF8.GetBytes(xmp));
                        #endregion

                        exif.Save(file);
#if DEBUG
                        // if using PngCs update metadata, so some XMP and Microsoft.Photo Date will lost,
                        // but, if using magick append XMP profile, PNG meta will lost.
                        //UpdatePngMetaInfo(fi, dm, meta);

                        //using (MagickImage image = new MagickImage(fi.FullName))
                        //{
                        //    if (image.FormatInfo.IsWritable)
                        //    {
                        //        var dm_png = dm.ToString("yyyy-MM-ddTHH:mm:ssZ");
                        //        var dm_xmp = dm.ToString("yyyy:MM:dd HH:mm:ss");
                        //        var dm_msxmp = dm.ToString("yyyy-MM-ddTHH:mm:ss.fff");
                        //        var dm_ms = dm.ToString("yyyy-MM-ddTHH:mm:sszzz");
                        //        var dm_misc = dm.ToString("yyyy:MM:dd HH:mm:sszzz");
                        //        foreach (var tag in tag_date)
                        //        {
                        //            try
                        //            {
                        //                var value_old = image.GetAttribute(tag);
                        //                if (tag.StartsWith("Microsoft")) { image.RemoveAttribute(tag); SetAttribute(image, tag, dm_ms); }
                        //                //else if (tag.StartsWith("xmp")) SetAttribute(image, tag, dm_xmp);
                        //                else if (tag.StartsWith("exif")) continue;
                        //                else SetAttribute(image, tag, dm_misc);
                        //            }
                        //            catch (Exception ex) { Log(ex.Message); }
                        //        }
                        //        //var xmp_profile = new XmpProfile(Encoding.UTF8.GetBytes(xmp));
                        //        //image.SetProfile(xmp_profile);
                        //        image.Write(fi.FullName, image.Format);
                        //    }
                        //}
#endif
                        fi.CreationTime = dc;
                        fi.LastWriteTime = dm;
                        fi.LastAccessTime = da;

                        //UpdatePngMetaInfo(fi, dm, meta);
                        Log($"File \"{file}\" touched with extra-method!");
                    }
                }
            }
            catch (Exception ex)
            {
                // Error occurred while reading image file
                Log($"File \"{file}\" touching failed!{Environment.NewLine}Error: {ex.Message}");
            }
        }

        public static void ShowMeta(string file, bool xmp_merge_nodes = false, bool show_xmp = true)
        {
            if (File.Exists(file))
            {
                var fi = new FileInfo(file);
                using (var ms = new FileStream(fi.FullName, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    try
                    {
                        ExifData exifdata = null;
                        try { exifdata = new ExifData(ms); } catch { }
                        ms.Seek(0, SeekOrigin.Begin);
                        using (MagickImage image = new MagickImage(ms))
                        {
                            if (image.FormatInfo.IsReadable)
                            {
                                if (exifdata is ExifData && image.Endian == Endian.Undefined) image.Endian = exifdata.ByteOrder == ExifByteOrder.BigEndian ? Endian.MSB : Endian.LSB;

                                var exif = image.HasProfile("exif") ? image.GetExifProfile() : new ExifProfile();
                                var exif_invalid = exif.InvalidTags;

                                #region Calc color Bit-Depth
                                var depth = image.Depth * image.ChannelCount;
                                if (image.ColorType == ColorType.Bilevel) depth = 2;
                                else if (image.ColorType == ColorType.Grayscale) depth = 8;
                                else if (image.ColorType == ColorType.GrayscaleAlpha) depth = 8 + 8;
                                else if (image.ColorType == ColorType.Palette) depth = (int)Math.Ceiling(Math.Log(image.ColormapSize, 2));
                                else if (image.ColorType == ColorType.PaletteAlpha) depth = (int)Math.Ceiling(Math.Log(image.ColormapSize, 2)) + 8;
                                else if (image.ColorType == ColorType.TrueColor) depth = 24;
                                else if (image.ColorType == ColorType.TrueColorAlpha) depth = 32;
                                else if (image.ColorType == ColorType.ColorSeparation) depth = 24;
                                else if (image.ColorType == ColorType.ColorSeparationAlpha) depth = 32;
                                #endregion

                                var cw = 35;
                                #region General Metadata
                                Log($"{"FileSize".PadRight(cw)}= {SmartFileSize(fi.Length)} [{fi.Length:N0} B]");
                                Log($"{"Dimensions".PadRight(cw)}= {image.Width}x{image.Height}x{depth}");
                                Log($"{"TotalPixels".PadRight(cw)}= {image.Width * image.Height / 1000.0 / 1000.0:F2} MegaPixels");
                                Log($"{"ColorSpace".PadRight(cw)}= {image.ColorSpace.ToString()}");
                                Log($"{"ColorType".PadRight(cw)}= {image.ColorType.ToString()}");
                                Log($"{"HasAlpha".PadRight(cw)}= {image.HasAlpha.ToString()}");
                                Log($"{"ColormapSize".PadRight(cw)}= {image.ColormapSize}");
                                //Log($"{"TotalColors".PadRight(cw)}= {image.TotalColors}");
                                Log($"{"FormatInfo".PadRight(cw)}= {image.FormatInfo.Format.ToString()}, MIME:{image.FormatInfo.MimeType}");
                                Log($"{"ByteOrder".PadRight(cw)}= {(exifdata is ExifData ? exifdata.ByteOrder.ToString() : image.Endian.ToString())}");
                                Log($"{"ClassType".PadRight(cw)}= {image.ClassType.ToString()}");
                                //Log($"{"Geometry".PadRight(cw)}= {image.Page.ToString()}");
                                Log($"{"Compression".PadRight(cw)}= {image.Compression.ToString()}");
                                if (image.FormatInfo.Format.ToString().Equals("jpeg", StringComparison.CurrentCultureIgnoreCase))
                                    Log($"{"Quality".PadRight(cw)}= {(image.Quality == 0 ? 75 : image.Quality)}");
                                Log($"{"Orientation".PadRight(cw)}= {image.Orientation.ToString()}");
                                Log($"{"Filter".PadRight(cw)}= {(image.FilterType == FilterType.Undefined ? "Adaptive" : image.FilterType.ToString())}");
                                Log($"{"Interlace".PadRight(cw)}= {image.Interlace.ToString()}");
                                Log($"{"Interpolate".PadRight(cw)}= {image.Interpolate.ToString()}");
                                if (image.Density != null)
                                {
                                    var is_ppi = image.Density.Units == DensityUnit.PixelsPerInch;
                                    var is_ppc = image.Density.Units == DensityUnit.PixelsPerCentimeter;
                                    var density = is_ppi ? image.Density : image.Density.ChangeUnits(DensityUnit.PixelsPerInch);
                                    var unit = is_ppi ? "PPI" : (is_ppc ? "PPC" : "UNK");
                                    if (is_ppi)
                                        Log($"{"Resolution/Density".PadRight(cw)}= {density.X:F0} PPI x {density.Y:F0} PPI");
                                    else
                                        Log($"{"Resolution/Density".PadRight(cw)}= {density.X:F0} PPI x {density.Y:F0} PPI [{image.Density.X:F2} {unit} x {image.Density.Y:F2} {unit}]");
                                }
                                #endregion
                                #region Attribures Metadata
                                foreach (var attr in image.AttributeNames)
                                {
                                    try
                                    {
                                        var value = GetAttribute(image, attr);
                                        if (string.IsNullOrEmpty(value)) continue;
                                        else if (attr.StartsWith("date:")) continue;
                                        else if (attr.Equals("png:bKGD")) value = image.BackgroundColor.ToString();
                                        else if (attr.Equals("png:cHRM"))
                                        {
                                            var cr = XYZ2RGB(image.ChromaRedPrimary.X, image.ChromaRedPrimary.Y, image.ChromaRedPrimary.Z);
                                            var cg = XYZ2RGB(image.ChromaGreenPrimary.X, image.ChromaGreenPrimary.Y, image.ChromaGreenPrimary.Z);
                                            var cb = XYZ2RGB(image.ChromaBluePrimary.X, image.ChromaBluePrimary.Y, image.ChromaBluePrimary.Z);

                                            var r = $"[{image.ChromaRedPrimary.X:F5},{image.ChromaRedPrimary.Y:F5},{image.ChromaRedPrimary.Z:F5}]";
                                            var g = $"[{image.ChromaGreenPrimary.X:F5},{image.ChromaGreenPrimary.Y:F5},{image.ChromaGreenPrimary.Z:F5}]";
                                            var b = $"[{image.ChromaBluePrimary.X:F5},{image.ChromaBluePrimary.Y:F5},{image.ChromaBluePrimary.Z:F5}]";
                                            value = $"R:{cr.ToString()}, G:{cg.ToString()}, B:{cb.ToString()}{Environment.NewLine}XYZ-R: {r}{Environment.NewLine}XYZ-G: {g}{Environment.NewLine}XYZ-B: {b}";
                                        }
                                        var values = value.Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
                                        if (attr.Equals("exif:ExtensibleMetadataPlatform", StringComparison.CurrentCultureIgnoreCase) && !show_xmp)
                                        {
                                            var text = $"{attr.PadRight(cw)}= { values.FirstOrDefault() } ...";
                                            Log(text);
                                            continue;
                                        }
                                        foreach (var v in values)
                                        {
                                            if (v.Length > 64) value = $"{v.Substring(0, 64)} ...";
                                            var text = v.Equals(values.First()) ? $"{attr.PadRight(cw)}= { v }" : $"{" ".PadRight(cw+2)}{ v }";
                                            Log(text);
                                        }
                                    }
                                    catch (Exception ex) { MessageBox.Show($"{attr} : {ex.Message}"); }
                                }
                                #endregion
                                #region Profiles List Metadata
                                Log($"{"Profiles".PadRight(cw)}= {string.Join(", ", image.ProfileNames)}");
                                foreach (var profile_name in image.ProfileNames)
                                {
                                    try
                                    {
                                        var profile = image.GetProfile(profile_name);
                                        var prefix = $"Profile {profile.Name}".PadRight(cw);
                                        var bytes = profile.ToByteArray().Select(b => $"{b}");
                                        Log($"{prefix}= {bytes.Count()} bytes");
                                    }
                                    catch (Exception ex) { Log(ex.Message); }
                                }
                                #endregion
                                #region XMP Metadata
                                var xmp = image.HasProfile("xmp") ? image.GetXmpProfile() : null;
                                if (xmp != null)
                                {
                                    var xml = Encoding.UTF8.GetString(xmp.GetData());

                                    var xml_doc = new XmlDocument();
                                    xml_doc.LoadXml(xml);
                                    foreach (XmlNode node in xml_doc.GetElementsByTagName("rdf:Description"))
                                    {
                                        foreach (XmlAttribute attr in node.Attributes)
                                        {
                                            if (string.IsNullOrEmpty(attr.Value)) continue;
                                            else if (attr.Name.StartsWith("xmlns:", StringComparison.CurrentCultureIgnoreCase)) continue;
                                            Log($"{$"  {attr.Name}".PadRight(cw)}= {attr.Value}");
                                        }
                                        foreach (XmlNode child in node.ChildNodes)
                                        {
                                            foreach (XmlAttribute attr in child.Attributes)
                                            {
                                                if (string.IsNullOrEmpty(attr.Value)) continue;
                                                else if (attr.Name.StartsWith("xmlns:", StringComparison.CurrentCultureIgnoreCase)) continue;
                                                Log($"{$"    {attr.Name}".PadRight(cw)}= {attr.Value}");
                                            }
                                            if (child.Name.Equals("dc:title", StringComparison.CurrentCultureIgnoreCase))
                                                Log($"{"    dc:Title".PadRight(cw)}= {child.InnerText}");
                                            else if (child.Name.Equals("xmp:creator") || child.Name.Equals("dc:creator") || child.Name.Equals("dc:rights") ||
                                                child.Name.Equals("dc:subject") || child.Name.StartsWith("MicrosoftPhoto:LastKeyword"))
                                            {
                                                var contents = new List<string>();
                                                foreach (XmlNode subchild in child.ChildNodes)
                                                {
                                                    if (subchild.Name.Equals("rdf:Bag") || subchild.Name.Equals("rdf:Seq"))
                                                    {
                                                        foreach (XmlNode li in subchild.ChildNodes) { contents.Add(li.InnerText.Trim()); }
                                                    }
                                                }
                                                Log($"{$"    {child.Name}".PadRight(cw)}= {string.Join("; ", contents)}");
                                            }
                                            else if (child.Name.Equals("MicrosoftPhoto:DateAcquired", StringComparison.CurrentCultureIgnoreCase))
                                                Log($"{"    MicrosoftPhoto:DateAcquired".PadRight(cw)}= {child.InnerText}");
                                            else if (child.Name.Equals("MicrosoftPhoto:DateTaken", StringComparison.CurrentCultureIgnoreCase))
                                                Log($"{"    MicrosoftPhoto:DateTaken".PadRight(cw)}= {child.InnerText}");
                                            else if (child.Name.Equals("exif:DateTimeDigitized", StringComparison.CurrentCultureIgnoreCase))
                                                Log($"{"    exif:DateTimeDigitized".PadRight(cw)}= {child.InnerText}");
                                            else if (child.Name.Equals("exif:DateTimeOriginal", StringComparison.CurrentCultureIgnoreCase))
                                                Log($"{"    exif:DateTimeOriginal".PadRight(cw)}= {child.InnerText}");
                                            else if (child.Name.Equals("tiff:DateTime", StringComparison.CurrentCultureIgnoreCase))
                                                Log($"{"    tiff:DateTime".PadRight(cw)}= {child.InnerText}");
                                            else
                                                Log($"{$"    {child.Name}".PadRight(cw)}= {child.InnerText}");
                                        }
                                    }
                                    if (show_xmp) Log($"{"  XML Contents".PadRight(cw)}= {FormatXML(xml, xmp_merge_nodes)}");
                                }
                                #endregion
                            }
                            else Log($"File \"{file}\" is not a read supported format!");
                        }
                    }
                    catch (Exception ex) { Log(ex.Message); }
                }
            }
            else Log($"File \"{file}\" not exists!");
        }
        #endregion

        #region Converting Image Format Helper
        private static System.Drawing.Color[] GetMatrix(System.Drawing.Bitmap bmp, int x, int y, int w, int h)
        {
            var ret = new List<System.Drawing.Color>();
            if (bmp is System.Drawing.Bitmap)
            {
                //var data = bmp.LockBits(new Rectangle(x, y, w, h), ImageLockMode.ReadOnly, bmp.PixelFormat);
                for (var i = x; i < x + w; i++)
                {
                    for (var j = y; j < y + h; j++)
                    {
                        if (i < bmp.Width && j < bmp.Height)
                            ret.Add(bmp.GetPixel(i, j));
                    }
                }
                //bmp.UnlockBits(data);
            }
            return (ret.ToArray());
        }

        private static bool GuessAlpha(Stream source, int window = 3, int threshold = 255)
        {
            var result = false;
            try
            {
                if (source is Stream && source.CanRead)
                {
                    var status = false;
                    if (source.CanSeek) source.Seek(0, SeekOrigin.Begin);
                    using (System.Drawing.Image image = System.Drawing.Image.FromStream(source))
                    {
                        if (image is System.Drawing.Image && (
                            image.RawFormat.Guid.Equals(System.Drawing.Imaging.ImageFormat.Png.Guid) ||
                            image.RawFormat.Guid.Equals(System.Drawing.Imaging.ImageFormat.Bmp.Guid) ||
                            image.RawFormat.Guid.Equals(System.Drawing.Imaging.ImageFormat.Tiff.Guid)))
                        {
                            if (image.PixelFormat == System.Drawing.Imaging.PixelFormat.Format32bppArgb) { status = true; }
                            else if (image.PixelFormat == System.Drawing.Imaging.PixelFormat.Format32bppPArgb) { status = true; }
                            else if (image.PixelFormat == System.Drawing.Imaging.PixelFormat.Format16bppArgb1555) { status = true; }
                            else if (image.PixelFormat == System.Drawing.Imaging.PixelFormat.Format64bppArgb) { status = true; }
                            else if (image.PixelFormat == System.Drawing.Imaging.PixelFormat.Format64bppPArgb) { status = true; }
                            else if (image.PixelFormat == System.Drawing.Imaging.PixelFormat.PAlpha) { status = true; }
                            else if (image.PixelFormat == System.Drawing.Imaging.PixelFormat.Alpha) { status = true; }
                            else if (System.Drawing.Image.IsAlphaPixelFormat(image.PixelFormat)) { status = true; }

                            if (status)
                            {
                                var bmp = new System.Drawing.Bitmap(image);
                                var w = bmp.Width;
                                var h = bmp.Height;
                                var m = window;
                                var mt = Math.Ceiling(m * m / 2.0);
                                var lt = GetMatrix(bmp, 0, 0, m, m).Count(c => c.A < threshold);
                                var rt = GetMatrix(bmp, w - m, 0, m, m).Count(c => c.A < threshold);
                                var lb = GetMatrix(bmp, 0, h - m, m, m).Count(c => c.A < threshold);
                                var rb = GetMatrix(bmp, w - m, h - m, m, m).Count(c => c.A < threshold);
                                var ct = GetMatrix(bmp, (int)(w / 2.0 - m / 2.0) , (int)(h / 2.0 - m / 2.0), m, m).Count(c => c.A < threshold);
                                status = (lt > mt || rt > mt || lb > mt || rb > mt || ct > mt) ? true : false;
                            }
                        }
                    }
                    result = status;
                }
            }
            catch (Exception ex) { Log(ex.Message); }
            return (result);
        }

        private static bool GuessAlpha(byte[] buffer, int window = 3, int threshold = 255)
        {
            var result = false;
            if (buffer is byte[] && buffer.Length > 0)
            {
                using (var ms = new MemoryStream(buffer))
                {
                    result = GuessAlpha(ms, window, threshold);
                }
            }
            return (result);
        }

        private static bool GuessAlpha(string file, int window = 3, int threshold = 255)
        {
            var result = false;

            if (File.Exists(file))
            {
                using (var ms = new MemoryStream(File.ReadAllBytes(file)))
                {
                    result = GuessAlpha(ms, window, threshold);
                }
            }
            return (result);
        }

        public string ConvertImageTo(string file, MagickFormat fmt, bool keep_name = false)
        {
            var result = file;
            if (File.Exists(file))
            {
                var fi = new FileInfo(file);
                var dc = fi.CreationTime;
                var dm = fi.LastWriteTime;
                var da = fi.LastAccessTime;

                using (var ms = new MemoryStream(File.ReadAllBytes(fi.FullName)))
                {
                    try
                    {
                        if (ConfirmYesToAll || !GuessAlpha(ms) || ShowConfirm($"Image File \"{fi.FullName}\" Has Alpha, {Environment.NewLine}Continue?", "Confirm"))
                        {
                            if (ms.CanSeek) ms.Seek(0, SeekOrigin.Begin);

                            ExifData exifdata = null;
                            try { exifdata = new ExifData(ms); } catch { };
                            if (ms.CanSeek) ms.Seek(0, SeekOrigin.Begin);

                            using (MagickImage image = new MagickImage(ms))
                            {
                                if (exifdata is ExifData && image.Endian == Endian.Undefined) image.Endian = exifdata.ByteOrder == ExifByteOrder.BigEndian ? Endian.MSB : Endian.LSB;

                                var meta = GetMetaInfo(image);

                                var fmt_info = MagickNET.SupportedFormats.Where(f => f.Format == fmt).FirstOrDefault();
                                var ext = fmt_info is MagickFormatInfo ? fmt_info.Format.ToString() : fmt.ToString();
                                var name = keep_name ? fi.FullName : Path.ChangeExtension(fi.FullName, $".{ext.ToLower()}");

                                #region touch software
                                var tag_software = "Software";
                                if (image.AttributeNames.Contains(tag_software) && !image.AttributeNames.Contains($"exif:{tag_software}"))
                                    SetAttribute(image, $"exif:{tag_software}", GetAttribute(image, tag_software));
                                if (!image.AttributeNames.Contains(tag_software) && image.AttributeNames.Contains($"exif:{tag_software}"))
                                    SetAttribute(image, tag_software, GetAttribute(image, $"exif:{tag_software}"));
                                #endregion

                                FixDPI(image);

                                //if (fmt == MagickFormat.Tif || fmt == MagickFormat.Tiff || fmt == MagickFormat.Tiff64)
                                //{
                                //    image.SetAttribute("tiff:alpha", "unassociated");
                                //    image.SetAttribute("tiff:photometric", "min-is-black");
                                //    image.SetAttribute("tiff:rows-per-strip", "512");
                                //}
                                image.Quality = ConvertQuality;
                                image.BackgroundColor = new MagickColor(ConvertBGColor.R, ConvertBGColor.G, ConvertBGColor.B, ConvertBGColor.A);
                                image.Write(name, fmt);

                                if (!keep_name && !name.Equals(fi.FullName, StringComparison.CurrentCultureIgnoreCase))
                                {
                                    var nfi = new FileInfo(name);
                                    nfi.CreationTime = dc;
                                    nfi.LastWriteTime = dm;
                                    nfi.LastAccessTime = da;

                                    Log($"Convert {file} => {name}");
                                }
                                else
                                    Log($"Convert {file} to {fmt_info.MimeType.ToString()}");

                                result = name;
                            }
                        }
                        else Log("Action Has Being Canceled!");
                    }
                    catch (Exception ex) { Log(ex.Message); }
                }
                fi.CreationTime = dc;
                fi.LastWriteTime = dm;
                fi.LastAccessTime = da;
            }
            else Log($"File \"{file}\" not exists!");
            return (result);
        }

        public void ConvertImagesTo(IEnumerable<string> files, MagickFormat fmt, bool keep_name = false)
        {
            if (files is IEnumerable<string>)
            {
                RunBgWorker(new Action<string, bool>((file, show_xmp) =>
                {
                    var ret = ConvertImageTo(file, fmt, keep_name);
                    if (!string.IsNullOrEmpty(ret) && File.Exists(ret) && !keep_name) AddFile(ret);
                }));
            }
        }

        public void ConvertImagesTo(MagickFormat fmt, bool keep_name = false)
        {
            if (FilesList.Items.Count >= 1)
            {
                List<string> files = new List<string>();
                foreach (var item in FilesList.SelectedItems.Count > 0 ? FilesList.SelectedItems : FilesList.Items) files.Add(item as string);
                ConvertImagesTo(files, fmt, keep_name);
            }
        }

        private System.Drawing.Imaging.ImageCodecInfo GetEncoderInfo(string mimeType)
        {
            // Get image codecs for all image formats 
            var codecs = System.Drawing.Imaging.ImageCodecInfo.GetImageEncoders();

            // Find the correct image codec 
            for (int i = 0; i < codecs.Length; i++)
            {
                if (codecs[i].MimeType == mimeType) return codecs[i];
            }

            return null;
        }

        public byte[] ReduceImageQuality(byte[] buffer, string fmt, int quality = 85)
        {
            byte[] result = buffer;
            try
            {
                if (buffer is byte[] && buffer.Length > 0)
                {
                    System.Drawing.Imaging.ImageFormat pFmt = System.Drawing.Imaging.ImageFormat.MemoryBmp;

                    fmt = fmt.ToLower();
                    if (fmt.Equals("png")) pFmt = System.Drawing.Imaging.ImageFormat.Png;
                    else if (fmt.Equals("jpg")) pFmt = System.Drawing.Imaging.ImageFormat.Jpeg;
                    else return (buffer);

                    if (!GuessAlpha(buffer))
                    {
                        using (var mi = new MemoryStream(buffer))
                        {
                            using (var mo = new MemoryStream())
                            {
                                var bmp = new System.Drawing.Bitmap(mi);
                                if (bmp is System.Drawing.Bitmap)
                                {
                                    var codec_info = GetEncoderInfo("image/jpeg");
                                    var qualityParam = new System.Drawing.Imaging.EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
                                    var encoderParams = new System.Drawing.Imaging.EncoderParameters(1);
                                    encoderParams.Param[0] = qualityParam;

                                    if (pFmt == System.Drawing.Imaging.ImageFormat.Jpeg)
                                    {
                                        var img = new System.Drawing.Bitmap(bmp.Width, bmp.Height, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
                                        img.SetResolution(bmp.HorizontalResolution, bmp.VerticalResolution);
                                        using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(img))
                                        {
                                            var bg = ConvertBGColor;
                                            g.Clear(System.Drawing.Color.FromArgb(bg.A, bg.R, bg.G, bg.B));
                                            g.DrawImage(bmp, 0, 0, new System.Drawing.Rectangle(new System.Drawing.Point(), bmp.Size), System.Drawing.GraphicsUnit.Pixel);
                                        }
                                        img.Save(mo, codec_info, encoderParams);
                                        img.Dispose();
                                    }
                                    else
                                        bmp.Save(mo, pFmt);

                                    result = mo.ToArray();
                                    bmp.Dispose();
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex) { Log(ex.Message); }
            return (result);
        }

        public string ReduceImageQuality(string file, string fmt, int quality = 75, bool keep_name = false)
        {
            string result = string.Empty;
            try
            {
                if (!string.IsNullOrEmpty(file) && File.Exists(file))
                {
                    var fi = new FileInfo(file);
                    var dc = fi.CreationTime;
                    var dm = fi.LastWriteTime;
                    var da = fi.LastAccessTime;

                    var fout = keep_name ? file : Path.ChangeExtension(file, $".{fmt}");

                    var bi = File.ReadAllBytes(file);
                    using (var msi = new MemoryStream(bi))
                    {
                        var exif_in = new ExifData(msi);
                        msi.Seek(0, SeekOrigin.Begin);

                        if (exif_in.ImageType == ImageType.Png)
                        {
                            #region Get & Update PNG metadata
                            msi.Seek(0, SeekOrigin.Begin);
                            DateTime dt;
                            var meta = GetPngMetaInfo(msi, full_field: false);
                            if (meta.ContainsKey("Creation Time") && DateTime.TryParse(Regex.Replace(meta["Creation Time"], @"^(\d{4}):(\d{2})\:(\d{2})(.*?)$", "$1/$2/$3T$4", RegexOptions.IgnoreCase), out dt))
                            {
                                if (!exif_in.TagExists(CompactExifLib.ExifTag.DateTime)) exif_in.SetDateChanged(dt);
                                if (!exif_in.TagExists(CompactExifLib.ExifTag.DateTimeDigitized)) exif_in.SetDateDigitized(dt);
                                if (!exif_in.TagExists(CompactExifLib.ExifTag.DateTimeOriginal)) exif_in.SetDateTaken(dt);
                            }
                            if (!exif_in.TagExists(CompactExifLib.ExifTag.XpTitle) && meta.ContainsKey("Title"))
                                exif_in.SetTagValue(CompactExifLib.ExifTag.XpTitle, meta["Title"], StrCoding.Utf16Le_Byte);
                            if (!exif_in.TagExists(CompactExifLib.ExifTag.XpSubject) && meta.ContainsKey("Subject"))
                                exif_in.SetTagValue(CompactExifLib.ExifTag.XpSubject, meta["Subject"], StrCoding.Utf16Le_Byte);
                            if (!exif_in.TagExists(CompactExifLib.ExifTag.XpAuthor) && meta.ContainsKey("Author"))
                            {
                                exif_in.SetTagValue(CompactExifLib.ExifTag.Artist, meta["Author"], StrCoding.Utf8);
                                exif_in.SetTagValue(CompactExifLib.ExifTag.XpAuthor, meta["Author"], StrCoding.Utf16Le_Byte);
                            }
                            if (!exif_in.TagExists(CompactExifLib.ExifTag.Copyright) && meta.ContainsKey("Copyright"))
                                exif_in.SetTagValue(CompactExifLib.ExifTag.Copyright, meta["Copyright"], StrCoding.Utf8);

                            if (!exif_in.TagExists(CompactExifLib.ExifTag.XpComment) && meta.ContainsKey("Description"))
                                exif_in.SetTagValue(CompactExifLib.ExifTag.XpComment, meta["Description"], StrCoding.Utf16Le_Byte);
                            if (!exif_in.TagExists(CompactExifLib.ExifTag.UserComment) && meta.ContainsKey("Description"))
                                exif_in.SetTagValue(CompactExifLib.ExifTag.UserComment, meta["Description"], StrCoding.IdCode_Utf16);

                            if (!exif_in.TagExists(CompactExifLib.ExifTag.XpKeywords) && meta.ContainsKey("Comment"))
                                exif_in.SetTagValue(CompactExifLib.ExifTag.XpKeywords, meta["Comment"], StrCoding.Utf16Le_Byte);

                            if (!exif_in.TagExists(CompactExifLib.ExifTag.FileSource) && meta.ContainsKey("Source"))
                                exif_in.SetTagValue(CompactExifLib.ExifTag.FileSource, meta["Source"], StrCoding.Utf8);

                            if (!exif_in.TagExists(CompactExifLib.ExifTag.Software) && meta.ContainsKey("Software"))
                                exif_in.SetTagValue(CompactExifLib.ExifTag.Software, meta["Software"], StrCoding.Utf8);

                            if (!exif_in.TagExists(CompactExifLib.ExifTag.XmpMetadata) && meta.ContainsKey("XML:com.adobe.xmp"))
                            {
                                var value = string.Join("", meta["XML:com.adobe.xmp"].Split(new char[]{ '\0' }).Last().ToArray().SkipWhile(c => c != '<'));
                                exif_in.SetTagRawData(CompactExifLib.ExifTag.XmpMetadata, ExifTagType.Byte, Encoding.UTF8.GetByteCount(value), Encoding.UTF8.GetBytes(value));
                            }
                            else if (!exif_in.TagExists(CompactExifLib.ExifTag.XmpMetadata) && meta.ContainsKey("Raw profile type xmp"))
                            {
                                var value = string.Join("", meta["Raw profile type xmp"].Split(new char[]{ '\0' }).Last().ToArray().SkipWhile(c => c != '<'));
                                exif_in.SetTagRawData(CompactExifLib.ExifTag.XmpMetadata, ExifTagType.Byte, Encoding.UTF8.GetByteCount(value), Encoding.UTF8.GetBytes(value));
                            }
                            #endregion
                        }
                        else
                        {
                            using (var image = new MagickImage(msi))
                            {
                                var fmt_jpg = new MagickFormat[] { MagickFormat.Jpg, MagickFormat.Jpeg, MagickFormat.Jpe };
                                if (fmt_jpg.Contains(image.Format) && (image.Quality < quality || Math.Abs(image.Quality - quality) <= 2))
                                {
                                    throw new WarningException($"Image Quality : {image.Quality} <= Reduce Quality : {quality}!");
                                }

                                var meta = GetMetaInfo(image);
                                if (meta is MetaInfo && meta.Profiles.ContainsKey("xmp") && !exif_in.TagExists(CompactExifLib.ExifTag.XmpMetadata))
                                {
                                    var xml = meta.Profiles["xmp"].GetData();
                                    exif_in.SetTagRawData(CompactExifLib.ExifTag.XmpMetadata, ExifTagType.Byte, xml.Length, xml);
                                }

                            }
                        }

                        var bo = ReduceImageQuality(bi, fmt, quality: quality);
                        if (bo is byte[] && bo.Length > 0)
                        {
                            using (var msp = new MemoryStream(bo))
                            {
                                var exif_out = new ExifData(msp);
                                exif_out.ReplaceAllTagsBy(exif_in);

                                DateTime dt;
                                if (exif_in.GetDateTaken(out dt)) { exif_out.SetDateTaken(dt); dc = dt; }
                                if (exif_in.GetDateDigitized(out dt)) { exif_out.SetDateDigitized(dt); dm = dt; }
                                if (exif_in.GetDateChanged(out dt)) { exif_out.SetDateChanged(dt); da = dt; }

                                using (var mso = new MemoryStream())
                                {
                                    msp.Seek(0, SeekOrigin.Begin);
                                    if (exif_out.TagExists(CompactExifLib.ExifTag.XmpMetadata))
                                    {
                                        using (var msx = new MemoryStream())
                                        {
                                            exif_out.Save(msp, msx);
                                            msx.Seek(0, SeekOrigin.Begin);
                                            using (var image = new MagickImage(msx))
                                            {
                                                ExifTagType type;
                                                int count;
                                                byte[] xmp;
                                                if (exif_out.GetTagRawData(CompactExifLib.ExifTag.XmpMetadata, out type, out count, out xmp))
                                                {
                                                    var xml = Encoding.UTF8.GetString(xmp);
                                                    image.SetProfile(new XmpProfile(xmp));
                                                    image.Write(mso, image.Format);
                                                }
                                            }
                                        }
                                    }
                                    else exif_out.Save(msp, mso);

                                    if (mso.Length < fi.Length) File.WriteAllBytes(fout, mso.ToArray());
                                }
                            }
                        }
                    }

                    var fo = new FileInfo(fout);
                    fo.CreationTime = dc;
                    fo.LastWriteTime = dm;
                    fo.LastAccessTime = da;

                    if (string.IsNullOrEmpty(fout))
                        Log($"Reduce {file} Size Failed!");
                    else
                    {
                        result = fout;
                        Log($"Reduce {file} From {SmartFileSize(fi.Length)} To {SmartFileSize(fo.Length)}!");
                    }
                }
            }
            catch (Exception ex) { Log(ex.Message); }
            return (result);
        }

        public string ReduceImageQuality(string file, MagickFormat fmt, int quality = 0, bool keep_name = false)
        {
            if (quality <= 0)
            {
                //int.TryParse(GetConfigValue(ReduceQualityKey, ReduceQuality), out ReduceQuality);
                return (ReduceImageQuality(file, fmt.ToString().ToLower(), ReduceQuality, keep_name));                
            }
            else
                return (ReduceImageQuality(file, fmt.ToString().ToLower(), quality, keep_name));
        }

        public void ReduceImageQuality(IEnumerable<string> files, MagickFormat fmt, int quality = 0, bool keep_name = false)
        {
            if (files is IEnumerable<string>)
            {
                RunBgWorker(new Action<string, bool>((file, show_xmp) =>
                {
                    var ret = ReduceImageQuality(file, fmt, quality, keep_name);
                    if (!string.IsNullOrEmpty(ret) && File.Exists(ret) && !keep_name) AddFile(ret);
                }));
            }
        }

        public void ReduceImageQuality(MagickFormat fmt = MagickFormat.Jpg, int quality = 0, bool keep_name = true)
        {
            if (FilesList.Items.Count >= 1)
            {
                List<string> files = new List<string>();
                foreach (var item in FilesList.SelectedItems.Count > 0 ? FilesList.SelectedItems : FilesList.Items) files.Add(item as string);
                ReduceImageQuality(files, fmt, quality, keep_name);
            }
        }
        #endregion

        #region Process SystemMenu
        /// <summary>
        /// 
        /// </summary>
        /// <param name="hWnd"></param>
        /// <param name="bRevert"></param>
        /// <returns></returns>
        [DllImport("user32.dll")]
        private static extern IntPtr GetSystemMenu(IntPtr hWnd, bool bRevert);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="hMenu"></param>
        /// <param name="wPosition"></param>
        /// <param name="wFlags"></param>
        /// <param name="wIDNewItem"></param>
        /// <param name="lpNewItem"></param>
        /// <returns></returns>
        [DllImport("user32.dll")]
        private static extern bool InsertMenu(IntPtr hMenu, int wPosition, int wFlags, int wIDNewItem, string lpNewItem);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="hMenu"></param>
        /// <param name="uItem"></param>
        /// <param name="fByPosition"></param>
        /// <param name="lpmii"></param>
        /// <returns></returns>
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool GetMenuItemInfo(IntPtr hMenu, uint uItem, bool fByPosition, MENUITEMINFO lpmii);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="hMenu"></param>
        /// <param name="uItem"></param>
        /// <param name="fByPosition"></param>
        /// <param name="lpmii"></param>
        /// <returns></returns>
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool SetMenuItemInfo(IntPtr hMenu, uint uItem, bool fByPosition, MENUITEMINFO lpmii);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="hMenu"></param>
        /// <param name="uItem"></param>
        /// <param name="uCheck"></param>
        /// <returns></returns>
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool CheckMenuItem(IntPtr hMenu, uint uItem, uint uCheck);

        /// <summary>
        /// 
        /// </summary>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private class MENUITEMINFO
        {
            public int cbSize;
            public uint fMask;
            public uint fType;
            public uint fState;
            public uint wID;
            public IntPtr hSubMenu;
            public IntPtr hbmpChecked;
            public IntPtr hbmpUnchecked;
            public IntPtr dwItemData;
            public IntPtr dwTypeData;
            public uint cch;
            public IntPtr hbmpItem;

            public MENUITEMINFO()
            {
                cbSize = Marshal.SizeOf(typeof(MENUITEMINFO));
            }
        }

        ///A window receives this message when the user chooses a command from the Window menu, 
        ///or when the user chooses the maximize button, minimize button, restore button, or close button.
        private const int WM_SYSCOMMAND = 0x0112;

        ///Draws a horizontal dividing line.This flag is used only in a drop-down menu, submenu, 
        ///or shortcut menu.The line cannot be grayed, disabled, or highlighted.
        private const int MF_SEPARATOR = 0x0800;

        private const int MFS_UNCHECKED = 0x0000;
        private const int MFS_CHECKED = 0x0008;

        ///Specifies that an ID is a position index into the menu and not a command ID.
        private const int MF_BYPOSITION = 0x4000;

        ///Specifies that the menu item is a text string.
        private const uint MF_STRING = 0x0000;

        ///Menu Ids for our custom menu items
        private const int _ItemTopMostMenuId = 1000;
        private const int _ItemAboutMenuID = 1001;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="topmost"></param>
        private void InitHookWndProc(bool topmost = false)
        {
            IntPtr windowhandle = new WindowInteropHelper(this).Handle;
            HwndSource hwndSource = HwndSource.FromHwnd(windowhandle);

            //Get the handle for the system menu
            IntPtr systemMenuHandle = GetSystemMenu(windowhandle, false);

            //Insert our custom menu items
            InsertMenu(systemMenuHandle, 5, MF_BYPOSITION | MF_SEPARATOR, 0, string.Empty); //Add a menu seperator
            if (topmost)
                InsertMenu(systemMenuHandle, 6, MF_BYPOSITION | MFS_CHECKED, _ItemTopMostMenuId, "Always On Top"); //Add a setting menu item
            else
                InsertMenu(systemMenuHandle, 6, MF_BYPOSITION | MFS_UNCHECKED, _ItemTopMostMenuId, "Always On Top"); //Add a setting menu item
            InsertMenu(systemMenuHandle, 7, MF_BYPOSITION, _ItemAboutMenuID, "About"); //add an About menu item

            hwndSource.AddHook(new HwndSourceHook(WndProc));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="topmost"></param>
        private void SetMenuTopMostState(bool topmost)
        {
            IntPtr windowhandle = new WindowInteropHelper(this).Handle;
            HwndSource hwndSource = HwndSource.FromHwnd(windowhandle);

            //Get the handle for the system menu
            IntPtr systemMenuHandle = GetSystemMenu(windowhandle, false);

            //            uint MIIM_STRING = 0x00000040;
            //            uint MIIM_STATE = 0x00000001;
            //            //uint MFT_STRING = 0x00000000;
            //            MENUITEMINFO miTopmost = new MENUITEMINFO() { fMask = MIIM_STATE, fType = MFS_UNCHECKED, dwTypeData = IntPtr.Zero };
            //            if (GetMenuItemInfo(systemMenuHandle, _ItemTopMostMenuId, false, miTopmost))
            //            {
            //                try
            //                {
            //                    miTopmost.cch++;
            //                    miTopmost.dwTypeData = Marshal.AllocHGlobal((IntPtr)(miTopmost.cch * 2));
            //                    if (GetMenuItemInfo(systemMenuHandle, _ItemTopMostMenuId, false, miTopmost))
            //                    {
            //                        string caption = Marshal.PtrToStringUni(miTopmost.dwTypeData);
            //#if DEBUG
            //                        if (miTopmost.fState == MFS_UNCHECKED)
            //                            miTopmost.fState = MFS_CHECKED;
            //                        else
            //                            miTopmost.fState = MFS_UNCHECKED;
            //#else
            //                        miTopmost.fState = (uint)(topmost ? MFS_CHECKED : MFS_UNCHECKED);
            //#endif
            //                        SetMenuItemInfo(systemMenuHandle, _ItemTopMostMenuId, false, miTopmost);
            //                        //PostMessage()
            //                    }
            //                }
            //                finally
            //                {
            //                    Marshal.FreeHGlobal(miTopmost.dwTypeData);
            //                }
            //            }
            CheckMenuItem(systemMenuHandle, _ItemTopMostMenuId, (uint)(topmost ? MFS_CHECKED : MFS_UNCHECKED));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="hwnd"></param>
        /// <param name="msg"></param>
        /// <param name="wParam"></param>
        /// <param name="lParam"></param>
        /// <param name="handled"></param>
        /// <returns></returns>
        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            // Check if the SystemCommand message has been executed
            if (msg == WM_SYSCOMMAND)
            {
                //check which menu item was clicked
                switch (wParam.ToInt32())
                {
                    case _ItemTopMostMenuId:
                        //MessageBox.Show("Item 1 was clicked");
                        AlwaysTopMost = !AlwaysTopMost;
                        Topmost = AlwaysTopMost;
                        SetConfigValue(AlwaysTopMostKey, AlwaysTopMost);
                        SetMenuTopMostState(AlwaysTopMost);
                        handled = true;
                        break;
                    case _ItemAboutMenuID:
                        //MessageBox.Show("Item 2 was clicked");
                        handled = true;
                        break;
                }
            }
            return IntPtr.Zero;
        }
        #endregion

        #region Common Helper
        private void InitMagicK()
        {
            try
            {
                var magick_cache = Path.IsPathRooted(CachePath) ? CachePath : Path.Combine(AppPath, CachePath);
                //if (!Directory.Exists(magick_cache)) Directory.CreateDirectory(magick_cache);
                //if (Directory.Exists(magick_cache)) MagickAnyCPU.CacheDirectory = magick_cache;
                if (Directory.Exists(magick_cache)) MagickNET.SetNativeLibraryDirectory(magick_cache);
                ResourceLimits.Memory = 256 * 1024 * 1024;
                ResourceLimits.LimitMemory(new Percentage(5));
                ResourceLimits.Thread = 4;
                //ResourceLimits.Area = 4096 * 4096;
                //ResourceLimits.Throttle = 
                OpenCL.IsEnabled = true;
                if (Directory.Exists(magick_cache)) OpenCL.SetCacheDirectory(magick_cache);
                //MagickNET.Initialize();
                //MagickNET.SupportedFormats
                SupportedFormats = ((MagickFormat[])Enum.GetValues(typeof(MagickFormat))).Select(e => $".{e.ToString().ToLower()}").ToList();
                SupportedFormats.AddRange(new string[] { ".spa", ".sph" });
            }
            catch (Exception ex) { Log(ex.Message); }
        }

        private void InitDefaultUI()
        {
            Dispatcher.InvokeAsync(() =>
            {
                var now = DateTime.Now;
                DateCreated.SelectedDate = now;
                DateModified.SelectedDate = now;
                DateAccessed.SelectedDate = now;
                DateCreated.IsTodayHighlighted = true;
                DateModified.IsTodayHighlighted = true;
                DateAccessed.IsTodayHighlighted = true;

                TimeCreated.Value = now;
                TimeModified.Value = now;
                TimeAccessed.Value = now;
                TimeCreated.DefaultValue = now;
                TimeModified.DefaultValue = now;
                TimeAccessed.DefaultValue = now;

                SetCreatedDateToAllEnabled.IsChecked = true;
                SetModifiedDateToAllEnabled.IsChecked = true;
                SetAccessedDateToAllEnabled.IsChecked = true;

                SetCreatedTimeToAllEnabled.IsChecked = true;
                SetModifiedTimeToAllEnabled.IsChecked = true;
                SetAccessedTimeToAllEnabled.IsChecked = true;

                FileRenameInputPopupCanvas.Background = Background;
                FileRenameInputPopupBorder.BorderBrush = FilesList.BorderBrush;
                FileRenameInputPopupBorder.BorderThickness = FilesList.BorderThickness;

                MetaInputPopupCanvas.Background = Background;
                MetaInputPopupBorder.BorderBrush = FilesList.BorderBrush;
                MetaInputPopupBorder.BorderThickness = FilesList.BorderThickness;
                MetaInputPopup.Width = Width - 28;
                MetaInputPopup.MinHeight = 336;
                //MetaInputPopup.Height = 336;

                MetaInputPopup.StaysOpen = true;
                MetaInputPopup.Placement = PlacementMode.Bottom;
                MetaInputPopup.HorizontalOffset = MetaInputPopup.Width - ShowMetaInputPopup.ActualWidth;
                MetaInputPopup.VerticalOffset = -6;

                MetaInputPopup.PreviewMouseDown += (obj, evt) => { Activate(); };

                CurrentMetaRating = 0;
            });
        }

        private void InitAccelerators()
        {
            // add keyboard accelerators for backwards navigation
            RoutedCommand cmd_Rename = new RoutedCommand();
            cmd_Rename.InputGestures.Add(new KeyGesture(Key.F2, ModifierKeys.None, Key.F2.ToString()));
            FilesList.CommandBindings.Add(new CommandBinding(cmd_Rename, (obj, evt) =>
            {
                evt.Handled = true;
                ShowRenamePanel();
            }));
            RenameSelected.InputGestureText = string.Join(", ", cmd_Rename.InputGestures.OfType<KeyGesture>().Select(k => k.DisplayString));

            RoutedCommand cmd_Remove = new RoutedCommand();
            cmd_Remove.InputGestures.Add(new KeyGesture(Key.Delete, ModifierKeys.None, Key.Delete.ToString()));
            FilesList.CommandBindings.Add(new CommandBinding(cmd_Remove, (obj, evt) =>
            {
                evt.Handled = true;
                RemoveFileListItems();
            }));
            RemoveSelected.InputGestureText = string.Join(", ", cmd_Remove.InputGestures.OfType<KeyGesture>().Select(k => k.DisplayString));

            RoutedCommand cmd_Display = new RoutedCommand();
            cmd_Display.InputGestures.Add(new KeyGesture(Key.Enter, ModifierKeys.None, Key.Enter.ToString()));
            FilesList.CommandBindings.Add(new CommandBinding(cmd_Display, (obj, evt) =>
            {
                evt.Handled = true;
                OpenFiles(Keyboard.Modifiers == ModifierKeys.Shift ? true : false);
            }));
            ViewSelected.InputGestureText = string.Join(", ", cmd_Display.InputGestures.OfType<KeyGesture>().Select(k => k.DisplayString));
        }

        private void PopupFlowWindowsLocation(Popup popup)
        {
            try
            {
                if (popup is Popup && popup.IsOpen)
                {
                    var offset_v = popup.VerticalOffset;
                    var offset_h = popup.HorizontalOffset;
                    popup.HorizontalOffset = offset_h + 1;
                    popup.HorizontalOffset = offset_h;
                    popup.VerticalOffset = offset_v + 1;
                    popup.VerticalOffset = offset_v;
                }
            }
            catch { }
        }

        private IEnumerable<string> GetDropedFiles(object sc)
        {
            var result = new List<string>();
            try
            {
                if ((sc is IEnumerable<string> && (sc as IEnumerable<string>).Count() > 0) ||
                    (sc is StringCollection && (sc as StringCollection).Count > 0))
                {
                    if (sc is StringCollection)
                    {
                        string[] sa = new string[(sc as StringCollection).Count];
                        (sc as StringCollection).CopyTo(sa, 0);
                        result.AddRange(sa);
                    }
                    else result.AddRange(sc as IEnumerable<string>);
                }
            }
            catch (Exception ex) { Log(ex.Message); }
            return (result);
        }

        private void SetTitle(string text = "")
        {
            Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    if (string.IsNullOrEmpty(text))
                        Title = $"{DefaultTitle} - [{FilesList.SelectedItems.Count}/{FilesList.Items.Count}]";
                    else
                        Title = $"{DefaultTitle} - {text}";
                }
                catch (Exception ex) { Log(ex.Message); }
            });
        }

        private void RemoveFileListItems()
        {
            try
            {
                var items = FilesList.SelectedItems.Count>0 ? FilesList.SelectedItems : FilesList.Items;
                foreach (var i in items.OfType<string>().ToList()) FilesList.Items.Remove(i);
            }
            catch (Exception ex) { ShowMessage(ex.Message, "ERROR"); }
        }

        private void OpenFiles(bool alt = false)
        {
            RunBgWorker(new Action<string, bool>((file, show_xmp) =>
            {
                if (alt)
                    Process.Start("OpenWith.exe", file);
                else
                    Process.Start(file);
            }), showlog: false);
        }

        private void ShowRenamePanel()
        {
            if (FilesList.SelectedItem != null)
            {
                var file = FilesList.SelectedItem as string;
                var filename = Path.GetFileName(file);
#if DEBUG
                var ret = ShowCustomForm(FileRenameInputPopupCanvas, filename);
                if (!ret.Equals(filename, StringComparison.CurrentCultureIgnoreCase))
                {
                    RenameFileName(file, ret);
                }
#else
                FileRenameInputNameText.Tag = file;
                FileRenameInputNameText.Text = filename;
                FileRenameInputPopup.StaysOpen = true;
                FileRenameInputPopup.IsOpen = true;
#endif
            }
        }

        private void RenameFileName(string file, string name)
        {
            if (File.Exists(file))
            {
                var folder = Path.GetDirectoryName(file);
                var fi = new FileInfo(file);
                var fn_new = name.Trim();
                if (!Path.IsPathRooted(fn_new)) fn_new = Path.GetFullPath(Path.Combine(fi.DirectoryName, fn_new));
                fi.MoveTo(fn_new);
                var idx = FilesList.Items.IndexOf(file);
                if (idx >= 0) FilesList.Items[idx] = fn_new;
            }
        }

        private void CloseRenamePanel()
        {
            FileRenameInputPopup.IsOpen = false;
            FileRenameInputNameText.Tag = null;
            FileRenameInputNameText.Text = string.Empty;
        }

        private void ShowMetaPanel()
        {
            MetaInputPopup.Tag = null;
            if (MetaInputPopup.StaysOpen)
                MetaInputPopup.IsOpen = !MetaInputPopup.IsOpen;
            else
                MetaInputPopup.IsOpen = true;
            MetaInputPopup.StaysOpen = Keyboard.Modifiers == ModifierKeys.Control;
            MetaInputPopup.Tag = MetaInputPopup.IsOpen && MetaInputPopup.StaysOpen ? (bool?)true : null;
        }
        #endregion

        public MainWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                Icon = new BitmapImage(new Uri("pack://application:,,,/TouchMeta;component/Resources/time.ico"));
            }
            catch (Exception ex) { ShowMessage(ex.Message); }

            try
            {
                bool.TryParse(GetConfigValue(AlwaysTopMostKey, AlwaysTopMost), out AlwaysTopMost);
                int.TryParse(GetConfigValue(ConvertQualityKey, ConvertQuality), out ConvertQuality);
                int.TryParse(GetConfigValue(ReduceQualityKey, ReduceQuality), out ReduceQuality);
                ConvertBGColor = (Color)ColorConverter.ConvertFromString(GetConfigValue(ConvertBGColorKey, ConvertBGColor));
            }
            catch (Exception ex) { ShowMessage($"Config Error!{Environment.NewLine}{ex.Message}"); }

            InitHookWndProc(AlwaysTopMost);
#if DEBUG
            WindowStartupLocation = WindowStartupLocation.Manual;
            Topmost = false;
#else
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Topmost = AlwaysTopMost;
#endif
            DefaultTitle = Title;

            InitMagicK();

            #region Default UI values
            InitDefaultUI();
            #endregion

            InitBgWorker();

            #region Add Keyboard Accelerators to ListBox
            InitAccelerators();
            #endregion

            #region Try hide popup when app not actived
            Deactivated += (obj, evt) =>
            {
                e.Handled = true;
                if (!Topmost && !IsFocused && WindowState != WindowState.Minimized)
                {
                    if (MetaInputPopup.Tag != null) { MetaInputPopup.IsOpen = false; }
                    if (FileRenameInputPopup.Tag != null) { FileRenameInputPopup.IsOpen = false; }
                }
                else { }
            };

            Activated += (obj, evt) =>
            {
                e.Handled = true;
                if (!Topmost && IsFocused)
                {
                    if (MetaInputPopup.Tag != null) { MetaInputPopup.IsOpen = true; }
                    if (FileRenameInputPopup.Tag != null) { FileRenameInputPopup.IsOpen = true; }
                }
            };
            #endregion

            Dispatcher.InvokeAsync(() => { ReduceToQuality.Value = ReduceQuality; });            

            var args = Environment.GetCommandLineArgs();
            LoadFiles(args.Skip(1).ToArray());
        }

        private void Window_LocationChanged(object sender, EventArgs e)
        {
            if (MetaInputPopup.IsOpen) PopupFlowWindowsLocation(MetaInputPopup);
            if (FileRenameInputPopup.IsOpen) PopupFlowWindowsLocation(FileRenameInputPopup);
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                if (FileRenameInputPopup.IsOpen) FileRenameInputPopup.IsOpen = false;
                if (MetaInputPopup.IsOpen) MetaInputPopup.IsOpen = false;
                //if (show)
            }
        }

        private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.MiddleButton == MouseButtonState.Pressed)
            {
                System.Threading.Thread.Sleep(200);
                Close();
            }
            else if (!IsActive) Activate();
        }

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            var fmts = e.Data.GetFormats(true);
#if DEBUG
            Debug.WriteLine(string.Join(", ", fmts));
#endif
            if (fmts.Contains("FileDrop") || fmts.Contains("Text") || fmts.Contains("Downloaded"))
            {
                e.Effects = DragDropEffects.Copy;
            }
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            var fmts = e.Data.GetFormats(true);
            if (fmts.Contains("Downloaded"))
            {
                Dispatcher.InvokeAsync(() =>
                {
                    var files = e.Data.GetData("Downloaded");
                    var dl = GetDropedFiles(files);
                    if (dl.Count() > 0) LoadFiles(dl);
                });
            }
            else if (fmts.Contains("FileDrop"))
            {
                Dispatcher.InvokeAsync(() =>
                {
                    var files = e.Data.GetData("FileDrop");
                    var dl = GetDropedFiles(files);
                    if (dl.Count() > 0) LoadFiles(dl);
                });
            }
            else if (fmts.Contains("Text"))
            {
                Dispatcher.InvokeAsync(() =>
                {
                    var files = (e.Data.GetData("Text") as string).Split();
                    if (files is IEnumerable<string> && files.Count() > 0)
                    {
                        LoadFiles((files as IEnumerable<string>).ToArray());
                    }
                });
            }
        }

        private bool _date_changed_ = false;
        private bool _time_changed_ = false;
        private void DateSelector_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_time_changed_)
                e.Handled = true;
            else
            {
                _date_changed_ = true;
                if (sender == DateCreated)
                {
                    var dt = GetCustomDateTime(DateCreated, TimeCreated);
                    DateCreated.SelectedDate = dt ?? DateCreated.SelectedDate;
                    TimeCreated.Value = DateCreated.SelectedDate;
                }
                else if (sender == DateModified)
                {
                    var dt = GetCustomDateTime(DateModified, TimeModified);
                    DateModified.SelectedDate = dt ?? DateModified.SelectedDate;
                    TimeModified.Value = DateModified.SelectedDate;
                }
                else if (sender == DateAccessed)
                {
                    var dt = GetCustomDateTime(DateAccessed, TimeAccessed);
                    DateAccessed.SelectedDate = dt ?? DateAccessed.SelectedDate;
                    TimeAccessed.Value = DateAccessed.SelectedDate;
                }
                UpdateFileTimeInfo();
                _date_changed_ = false;
            }
        }

        private void TimeSelector_ValueChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (_date_changed_)
                e.Handled = true;
            else
            {
                _time_changed_ = true;
                if (sender == TimeCreated)
                {
                    var dt = GetCustomDateTime(DateCreated, TimeCreated);
                    DateCreated.SelectedDate = dt ?? DateCreated.SelectedDate;
                    TimeCreated.Value = DateCreated.SelectedDate;
                }
                else if (sender == TimeModified)
                {
                    var dt = GetCustomDateTime(DateModified, TimeModified);
                    DateModified.SelectedDate = dt ?? DateModified.SelectedDate;
                    TimeModified.Value = DateModified.SelectedDate;
                }
                else if (sender == TimeAccessed)
                {
                    var dt = GetCustomDateTime(DateAccessed, TimeAccessed);
                    DateAccessed.SelectedDate = dt ?? DateAccessed.SelectedDate;
                    TimeAccessed.Value = DateAccessed.SelectedDate;
                }
                UpdateFileTimeInfo();
                _time_changed_ = false;
            }
        }

        private void FilesList_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                e.Handled = true;
                var force = Keyboard.Modifiers == ModifierKeys.Shift;
                var pos = e.GetPosition(this);
                if (force || pos.X < 0 || pos.X > this.Width || pos.Y < 0 || pos.Y > this.Height)
                {
                    try
                    {
                        var files = FilesList.SelectedItems.Count > 0 ? FilesList.SelectedItems : FilesList.Items;
                        var fl = new string[files.Count];
                        files.CopyTo(fl, 0);

                        var fdl = new StringCollection();
                        fdl.AddRange(fl);
                        var dp = new DataObject(fdl);
                        dp.SetFileDropList(fdl);
                        DragDrop.DoDragDrop(this, dp, DragDropEffects.Copy);
                    }
                    catch (Exception ex) { Log(ex.Message); }
                }
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"POS: X => {pos.X}, Y => {pos.Y}");
#endif
            }
        }

        private void FilesList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                var alt = Keyboard.Modifiers == ModifierKeys.Shift;
                OpenFiles(alt);
            }
        }

        private void FilesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            e.Handled = true;
            if (FilesList.SelectedItem != null)
            {
                var file = FilesList.SelectedItem as string;
                UpdateFileTimeInfo(file);
            }
            SetTitle();
        }

        private void FilesListAction_Click(object sender, RoutedEventArgs e)
        {
            var force = Keyboard.Modifiers == ModifierKeys.Control;
            var alt = Keyboard.Modifiers == ModifierKeys.Shift;
            #region Get Image File(s) Time
            if (sender == GetFileTimeFromSelected)
            {
                if (FilesList.SelectedItem != null)
                {
                    #region Get File DateTime From Selected File
                    var file = FilesList.SelectedItem as string;
                    if (File.Exists(file))
                    {
                        var fi = new FileInfo(file);
                        SetCustomDateTime(dtc: fi.CreationTime, dtm: fi.LastWriteTime, dta: fi.LastAccessTime);
                    }
                    #endregion
                }
            }
            else if (sender == GetMetaTimeFromSelected)
            {
                if (FilesList.SelectedItem != null)
                {
                    #region Get Metadata DateTime From Selected File
                    var file = FilesList.SelectedItem as string;
                    var dt = GetMetaTime(file);
                    if (dt != null) SetCustomDateTime(dt: dt);
                    #endregion
                }
            }
            else if (sender == GetFileTimeFromFilaName)
            {
                if (FilesList.SelectedItem != null)
                {
                    #region Touching File Time
                    var meta = CurrentMeta;

                    RunBgWorker(new Action<string, bool>((file, show_xmp) =>
                    {
                        SetCustomDateTime(dt: ParseDateTime(file));
                    }));
                    #endregion
                }
            }
            #endregion
            #region Get Image File(s) Metadata
            else if (sender == GetMetaInfoFromSelected)
            {
                if (FilesList.SelectedItem != null)
                {
                    #region Get Metadata Infomation From Selected File
                    var file = FilesList.SelectedItem as string;
                    CurrentMeta = GetMetaInfo(file);
                    #endregion
                }
            }
            else if (sender == GetClipboardMetaInfo)
            {
                #region Get Metadata Infomation From Clipboard
                CurrentMeta = GetMetaInfoFromClipboard(CurrentMeta);
                #endregion
            }
            else if (sender == SetClipboardMetaInfo)
            {
                #region Get Metadata Infomation From Clipboard
                SetMetaInfoToClipboard(CurrentMeta);
                #endregion
            }
            #endregion
            #region Set Image File(s) Time
            else if (sender == SetFileTimeFromFileName)
            {
                #region Touching File Time
                var meta = CurrentMeta;

                RunBgWorker(new Action<string, bool>((file, show_xmp) =>
                {
                    TouchDate(file, force: force, dtm: ParseDateTime(file), meta: meta);
                }));
                #endregion
            }
            else if (sender == SetFileTimeFromC)
            {
                #region Touching File Time
                RunBgWorker(new Action<string, bool>((file, show_xmp) =>
                {
                    var dt = File.GetCreationTime(file);
                    var meta = new MetaInfo() { DateCreated = dt, DateModified = dt, DateAccesed = dt };
                    TouchDate(file, force: true, meta: meta);
                }));
                #endregion
            }
            else if (sender == SetFileTimeFromM)
            {
                #region Touching File Time
                RunBgWorker(new Action<string, bool>((file, show_xmp) =>
                {
                    var dt = File.GetLastWriteTime(file);
                    var meta = new MetaInfo() { DateCreated = dt, DateModified = dt, DateAccesed = dt };
                    TouchDate(file, force: true, meta: meta);
                }));
                #endregion
            }
            else if (sender == SetFileTimeFromA)
            {
                #region Touching File Time
                RunBgWorker(new Action<string, bool>((file, show_xmp) =>
                {
                    var dt = File.GetLastAccessTime(file);
                    var meta = new MetaInfo() { DateCreated = dt, DateModified = dt, DateAccesed = dt };
                    TouchDate(file, force: true, meta: meta);
                }));
                #endregion
            }
            else if (sender == SetFileTimeMeta)
            {
                #region Touching File Time
                RunBgWorker(new Action<string, bool>((file, show_xmp) =>
                {
                    var dt = File.GetLastWriteTime(file);
                    var meta = new MetaInfo() { DateCreated = dt, DateModified = dt, DateAccesed = dt };
                    TouchDate(file, force: false, meta: meta);
                }));
                #endregion
            }
            else if (sender == SetFolderTimeFromFM)
            {
                #region Touching File Time
                RunBgWorker(new Action<string, bool>((file, show_xmp) =>
                {
                    var dt = File.GetLastWriteTime(file);
                    var meta = new MetaInfo() { DateCreated = dt, DateModified = dt, DateAccesed = dt };
                    TouchFolder(Path.GetDirectoryName(file), force: force, meta: meta);
                }));
                #endregion
            }
            #endregion
            #region Set Image File(s) Metadata
            else if (sender == TouchMetaFromC)
            {
                #region Touching File Time
                RunBgWorker(new Action<string, bool>((file, show_xmp) =>
                {
                    TouchMetaDate(file, alt ? CurrentMeta.DateCreated : File.GetCreationTime(file));
                }));
                #endregion
            }
            else if (sender == TouchMetaFromM)
            {
                #region Touching File Time
                RunBgWorker(new Action<string, bool>((file, show_xmp) =>
                {
                    //#if DEBUG
                    TouchMetaDate(file, alt ? CurrentMeta.DateModified : File.GetLastWriteTime(file));
                    //#else
                    //                    TouchMeta(file, force: force, dtm: File.GetLastWriteTime(file), meta: meta);
                    //#endif
                }));
                #endregion
            }
            else if (sender == TouchMetaFromA)
            {
                #region Touching File Time
                RunBgWorker(new Action<string, bool>((file, show_xmp) =>
                {
                    TouchMetaDate(file, alt ? CurrentMeta.DateAccesed : File.GetLastAccessTime(file));
                }));
                #endregion
            }
            else if (sender == ReTouchMeta)
            {
                #region Re-Touch Metadate via MagicK.Net
                RunBgWorker(new Action<string, bool>((file, show_xmp) =>
                {
                    var meta = GetMetaInfo(file);
                    if (force)
                    {
                        meta.RatingPercent = CurrentMetaRating;
                        meta.Rating = RatingToRanking(meta.RatingPercent);
                    }
                    TouchMeta(file, force: true, meta: meta);
                }));
                #endregion
            }
            else if (sender == ReTouchMetaAlt)
            {
                #region Re-Touch Metadata via CompactExifLib
                RunBgWorker(new Action<string, bool>((file, show_xmp) =>
                {
                    var meta = GetMetaInfo(file);
                    if (force)
                    {
                        meta.RatingPercent = CurrentMetaRating;
                        meta.Rating = RatingToRanking(meta.RatingPercent);
                    }
                    TouchMetaAlt(file, force: true, meta: meta, pngcs: alt);
                }));
                #endregion
            }
            #endregion
            #region Add/Remove/Replace/Empty Image File(s) Metadata
            #region Title
            else if (sender == ChangeMetaTitleAppend)
            {
                RunBgWorker(new Action<string, bool>((file, show_xmp) =>
                {
                    ChangeProperties(file, CurrentMeta, ChangePropertyType.Title, ChangePropertyMode.Append);
                }));
            }
            else if (sender == ChangeMetaTitleRemove)
            {
                RunBgWorker(new Action<string, bool>((file, show_xmp) =>
                {
                    ChangeProperties(file, CurrentMeta, ChangePropertyType.Title, ChangePropertyMode.Remove);
                }));
            }
            else if (sender == ChangeMetaTitleReplace)
            {
                RunBgWorker(new Action<string, bool>((file, show_xmp) =>
                {
                    ChangeProperties(file, CurrentMeta, ChangePropertyType.Title, ChangePropertyMode.Replace);
                }));
            }
            else if (sender == ChangeMetaTitleEmpty)
            {
                RunBgWorker(new Action<string, bool>((file, show_xmp) =>
                {
                    ChangeProperties(file, CurrentMeta, ChangePropertyType.Title, ChangePropertyMode.Empty);
                }));
            }
            #endregion
            #region Subject
            else if (sender == ChangeMetaSubjectAppend)
            {
                RunBgWorker(new Action<string, bool>((file, show_xmp) =>
                {
                    ChangeProperties(file, CurrentMeta, ChangePropertyType.Subject, ChangePropertyMode.Append);
                }));
            }
            else if (sender == ChangeMetaSubjectRemove)
            {
                RunBgWorker(new Action<string, bool>((file, show_xmp) =>
                {
                    ChangeProperties(file, CurrentMeta, ChangePropertyType.Subject, ChangePropertyMode.Remove);
                }));
            }
            else if (sender == ChangeMetaSubjectReplace)
            {
                RunBgWorker(new Action<string, bool>((file, show_xmp) =>
                {
                    ChangeProperties(file, CurrentMeta, ChangePropertyType.Subject, ChangePropertyMode.Replace);
                }));
            }
            else if (sender == ChangeMetaSubjectEmpty)
            {
                RunBgWorker(new Action<string, bool>((file, show_xmp) =>
                {
                    ChangeProperties(file, CurrentMeta, ChangePropertyType.Subject, ChangePropertyMode.Empty);
                }));
            }
            #endregion
            #region Keywords
            else if (sender == ChangeMetaKeywordsAppend)
            {
                RunBgWorker(new Action<string, bool>((file, show_xmp) =>
                {
                    ChangeProperties(file, CurrentMeta, ChangePropertyType.Keywords, ChangePropertyMode.Append);
                }));
            }
            else if (sender == ChangeMetaKeywordsRemove)
            {
                RunBgWorker(new Action<string, bool>((file, show_xmp) =>
                {
                    ChangeProperties(file, CurrentMeta, ChangePropertyType.Keywords, ChangePropertyMode.Remove);
                }));
            }
            else if (sender == ChangeMetaKeywordsReplace)
            {
                RunBgWorker(new Action<string, bool>((file, show_xmp) =>
                {
                    ChangeProperties(file, CurrentMeta, ChangePropertyType.Keywords, ChangePropertyMode.Replace);
                }));
            }
            else if (sender == ChangeMetaKeywordsEmpty)
            {
                RunBgWorker(new Action<string, bool>((file, show_xmp) =>
                {
                    ChangeProperties(file, CurrentMeta, ChangePropertyType.Keywords, ChangePropertyMode.Empty);
                }));
            }
            #endregion
            #region Comment
            else if (sender == ChangeMetaCommentAppend)
            {
                RunBgWorker(new Action<string, bool>((file, show_xmp) =>
                {
                    ChangeProperties(file, CurrentMeta, ChangePropertyType.Comment, ChangePropertyMode.Append);
                }));
            }
            else if (sender == ChangeMetaCommentRemove)
            {
                RunBgWorker(new Action<string, bool>((file, show_xmp) =>
                {
                    ChangeProperties(file, CurrentMeta, ChangePropertyType.Comment, ChangePropertyMode.Remove);
                }));
            }
            else if (sender == ChangeMetaCommentReplace)
            {
                RunBgWorker(new Action<string, bool>((file, show_xmp) =>
                {
                    ChangeProperties(file, CurrentMeta, ChangePropertyType.Comment, ChangePropertyMode.Replace);
                }));
            }
            else if (sender == ChangeMetaCommentEmpty)
            {
                RunBgWorker(new Action<string, bool>((file, show_xmp) =>
                {
                    ChangeProperties(file, CurrentMeta, ChangePropertyType.Comment, ChangePropertyMode.Empty);
                }));
            }
            #endregion
            #region Authors
            else if (sender == ChangeMetaAuthorsAppend)
            {
                RunBgWorker(new Action<string, bool>((file, show_xmp) =>
                {
                    ChangeProperties(file, CurrentMeta, ChangePropertyType.Authors, ChangePropertyMode.Append);
                }));
            }
            else if (sender == ChangeMetaAuthorsRemove)
            {
                RunBgWorker(new Action<string, bool>((file, show_xmp) =>
                {
                    ChangeProperties(file, CurrentMeta, ChangePropertyType.Authors, ChangePropertyMode.Remove);
                }));
            }
            else if (sender == ChangeMetaAuthorsReplace)
            {
                RunBgWorker(new Action<string, bool>((file, show_xmp) =>
                {
                    ChangeProperties(file, CurrentMeta, ChangePropertyType.Authors, ChangePropertyMode.Replace);
                }));
            }
            else if (sender == ChangeMetaAuthorsEmpty)
            {
                RunBgWorker(new Action<string, bool>((file, show_xmp) =>
                {
                    ChangeProperties(file, CurrentMeta, ChangePropertyType.Authors, ChangePropertyMode.Empty);
                }));
            }
            #endregion
            #region Copyrights
            else if (sender == ChangeMetaCopyrightsAppend)
            {
                RunBgWorker(new Action<string, bool>((file, show_xmp) =>
                {
                    ChangeProperties(file, CurrentMeta, ChangePropertyType.Copyrights, ChangePropertyMode.Append);
                }));
            }
            else if (sender == ChangeMetaCopyrightsRemove)
            {
                RunBgWorker(new Action<string, bool>((file, show_xmp) =>
                {
                    ChangeProperties(file, CurrentMeta, ChangePropertyType.Copyrights, ChangePropertyMode.Remove);
                }));
            }
            else if (sender == ChangeMetaCopyrightsReplace)
            {
                RunBgWorker(new Action<string, bool>((file, show_xmp) =>
                {
                    ChangeProperties(file, CurrentMeta, ChangePropertyType.Copyrights, ChangePropertyMode.Replace);
                }));
            }
            else if (sender == ChangeMetaCopyrightsEmpty)
            {
                RunBgWorker(new Action<string, bool>((file, show_xmp) =>
                {
                    ChangeProperties(file, CurrentMeta, ChangePropertyType.Copyrights, ChangePropertyMode.Empty);
                }));
            }
            #endregion
            #region Rating
            else if (sender == ChangeMetaRatingAppend)
            {
                RunBgWorker(new Action<string, bool>((file, show_xmp) =>
                {
                    ChangeProperties(file, CurrentMeta, ChangePropertyType.Rating, ChangePropertyMode.Append);
                }));
            }
            else if (sender == ChangeMetaRatingRemove)
            {
                RunBgWorker(new Action<string, bool>((file, show_xmp) =>
                {
                    ChangeProperties(file, CurrentMeta, ChangePropertyType.Rating, ChangePropertyMode.Remove);
                }));
            }
            else if (sender == ChangeMetaRatingReplace)
            {
                RunBgWorker(new Action<string, bool>((file, show_xmp) =>
                {
                    ChangeProperties(file, CurrentMeta, ChangePropertyType.Rating, ChangePropertyMode.Replace);
                }));
            }
            else if (sender == ChangeMetaRatingEmpty)
            {
                RunBgWorker(new Action<string, bool>((file, show_xmp) =>
                {
                    ChangeProperties(file, CurrentMeta, ChangePropertyType.Rating, ChangePropertyMode.Empty);
                }));
            }
            #endregion
            #region Smart
            else if (sender == ChangeMetaSmartAppend)
            {
                RunBgWorker(new Action<string, bool>((file, show_xmp) =>
                {
                    ChangeProperties(file, CurrentMeta, ChangePropertyType.Smart, ChangePropertyMode.Append);
                }));
            }
            else if (sender == ChangeMetaSmartRemove)
            {
                RunBgWorker(new Action<string, bool>((file, show_xmp) =>
                {
                    ChangeProperties(file, CurrentMeta, ChangePropertyType.Smart, ChangePropertyMode.Remove);
                }));
            }
            else if (sender == ChangeMetaSmartReplace)
            {
                RunBgWorker(new Action<string, bool>((file, show_xmp) =>
                {
                    ChangeProperties(file, CurrentMeta, ChangePropertyType.Smart, ChangePropertyMode.Replace);
                }));
            }
            else if (sender == ChangeMetaSmartEmpty)
            {
                RunBgWorker(new Action<string, bool>((file, show_xmp) =>
                {
                    ChangeProperties(file, CurrentMeta, ChangePropertyType.Smart, ChangePropertyMode.Empty);
                }));
            }
            #endregion
            #region 
            #endregion
            #endregion
            #region Convert Image File Format
            else if (sender == ConvertSelectedToJpg)
            {
                ConvertImagesTo(MagickFormat.Jpg, keep_name: alt);
            }
            else if (sender == ConvertSelectedToPng)
            {
                ConvertImagesTo(MagickFormat.Png, keep_name: alt);
            }
            else if (sender == ConvertSelectedToGif)
            {
                ConvertImagesTo(MagickFormat.Gif, keep_name: alt);
            }
            else if (sender == ConvertSelectedToPdf)
            {
                ConvertImagesTo(MagickFormat.Pdf, keep_name: alt);
            }
            else if (sender == ConvertSelectedToTif)
            {
                ConvertImagesTo(MagickFormat.Tiff, keep_name: alt);
            }
            else if (sender == ConvertSelectedToAvif)
            {
                ConvertImagesTo(MagickFormat.Avif, keep_name: alt);
            }
            else if (sender == ConvertSelectedToWebp)
            {
                ConvertImagesTo(MagickFormat.WebP, keep_name: alt);
            }
            #endregion
            #region View/Rename/Reduce Image File(s)
            else if (sender == ViewSelected)
            {
                OpenFiles(alt);
            }
            else if (sender == RenameSelected)
            {
                ShowRenamePanel();
            }
            else if (sender == ReduceSelected)
            {
                ReduceImageQuality(MagickFormat.Jpg, keep_name: true);
            }
            else if (sender == ReduceToSelected)
            {
                ReduceImageQuality(MagickFormat.Jpg, quality: Convert.ToInt32(ReduceToQuality.Value), keep_name: true);
            }
            #endregion
            #region Add/Remove Image File(s) From List
            else if (sender == AddFromClipboard)
            {
                #region Add Files From Clipboard
                try
                {
                    string[] files = new string[] { };
                    if (Clipboard.ContainsFileDropList())
                    {
                        var flist = Clipboard.GetFileDropList();
                        files = new string[flist.Count];
                        flist.CopyTo(files, 0);
                    }
                    else if (Clipboard.ContainsText())
                    {
                        files = Clipboard.GetText().Split();
                    }

                    if (files is IEnumerable<string> && files.Count() > 0)
                    {
                        Dispatcher.InvokeAsync(() =>
                        {
                            LoadFiles((files as IEnumerable<string>).ToArray());
                        });
                    }
                }
                catch (Exception ex) { ShowMessage(ex.Message, "ERROR"); }
                #endregion
            }
            else if (sender == RemoveSelected)
            {
                #region From Files List Remove Selected Files
                RemoveFileListItems();
                #endregion
            }
            else if (sender == RemoveAll)
            {
                FilesList.Items.Clear();
            }
            #endregion
        }

        private void BtnAction_Click(object sender, RoutedEventArgs e)
        {
            if (sender == SetCreatedDateTimeToAll)
            {
                #region Set Created DateTime To All
                SetCustomDateTime(dtm: DateCreated.SelectedDate, dta: DateCreated.SelectedDate);
                #endregion
            }
            else if (sender == SetModifiedDateTimeToAll)
            {
                #region Set Modified DateTime To All
                SetCustomDateTime(dtc: DateModified.SelectedDate, dta: DateModified.SelectedDate);
                #endregion
            }
            else if (sender == SetAccessedDateTimeToAll)
            {
                #region Set Accessed DateTime To All
                SetCustomDateTime(dtc: DateAccessed.SelectedDate, dtm: DateAccessed.SelectedDate);
                #endregion
            }
            else if (sender == ShowMetaInputPopup)
            {
                #region Popup Metadata Input Panel
                ShowMetaPanel();
                #endregion
            }
            else if (sender == TimeStringParsing)
            {
                #region Parsing DateTime
                var dt = DateTime.Now;
                var dt_text = NormalizeDateTimeText(TimeStringContent.Text);
                var fdt = ParseDateTime(dt_text, is_file: false);
                if (fdt is DateTime || DateTime.TryParse(dt_text, out dt)) SetCustomDateTime(fdt ?? dt);
                #endregion
            }
            else if (sender == BtnTouchTime)
            {
                #region Touching File Time
                var force = Keyboard.Modifiers == ModifierKeys.Control;
                var meta = CurrentMeta;

                RunBgWorker(new Action<string, bool>((file, show_xmp) =>
                {
                    TouchDate(file, force: force, meta: meta);
                }));
                #endregion
            }
            else if (sender == BtnTouchMeta)
            {
                #region Touching Metadata
                var force = Keyboard.Modifiers == ModifierKeys.Control;
                var meta = CurrentMeta;

                RunBgWorker(new Action<string, bool>((file, show_xmp) =>
                {
                    meta.ShowXMP = show_xmp;
                    TouchMeta(file, force: force, meta: meta);
                }));
                #endregion
            }
            else if (sender == BtnClearMeta)
            {
                #region Clear Metadata
                RunBgWorker(new Action<string, bool>((file, show_xmp) =>
                {
                    ClearMeta(file);
                }));
                #endregion
            }
            else if (sender == BtnShowMeta)
            {
                if (Keyboard.Modifiers == ModifierKeys.Alt || Mouse.XButton1 == MouseButtonState.Pressed)
                {
                    SetMetaInfoToClipboard(CurrentMeta);
                }
                #region Show Metadata
                bool xmp_merge_nodes = Keyboard.Modifiers == ModifierKeys.Control;
                RunBgWorker(new Action<string, bool>((file, show_xmp) =>
                {
                    ShowMeta(file, xmp_merge_nodes, show_xmp: show_xmp);
                }));
                #endregion
            }
            else if (sender == BtnAddFile)
            {
                LoadFiles();
            }
            else if (sender == FileRenameInputClose)
            {
                CloseRenamePanel();
            }
            else if (sender == FileRenameApply)
            {
                #region Rename Selected File
                if (FileRenameInputNameText.Tag is string)
                {
                    try
                    {
                        var file = FileRenameInputNameText.Tag as string;
                        RenameFileName(file, FileRenameInputNameText.Text);
                    }
                    catch (Exception ex) { ShowMessage(ex.Message); }
                }
                FileRenameInputPopup.IsOpen = false;
                #endregion
            }
            else if (sender == MetaInputRanking0)
            {
                CurrentMetaRating = 0;
            }
            else if (sender == MetaInputRanking1)
            {
                CurrentMetaRating = 1;
            }
            else if (sender == MetaInputRanking2)
            {
                CurrentMetaRating = 25;
            }
            else if (sender == MetaInputRanking3)
            {
                CurrentMetaRating = 50;
            }
            else if (sender == MetaInputRanking4)
            {
                CurrentMetaRating = 75;
            }
            else if (sender == MetaInputRanking5)
            {
                CurrentMetaRating = 99;
            }
            else if (sender == ShowHelp)
            {
                if (Keyboard.Modifiers == ModifierKeys.Alt || Mouse.XButton1 == MouseButtonState.Pressed)
                {
                    ShowLog();
                }
                else if (Keyboard.Modifiers == ModifierKeys.Shift || Mouse.XButton2 == MouseButtonState.Pressed)
                {
                    var fl = FilesList.SelectedItems.Count > 0 ? FilesList.SelectedItems : FilesList.Items;
                    var fa = new string[fl.Count];
                    fl.CopyTo(fa, 0);
                    Clipboard.SetText(string.Join(Environment.NewLine, fa));
                }
                else
                {
                    var lines = new List<string>();
                    lines.Add("Usage");
                    lines.Add("-".PadRight(NormallyMessageWidth, '-'));

                    lines.Add("Ctrl+Click Touch Time Button : Force Touching DateTime");
                    lines.Add("Ctrl+Click Touch Meta Button : Force Touching Metadata");

                    lines.Add("~".PadRight(NormallyMessageWidth, '~'));
                    lines.Add("Note:");
                    lines.Add("Convert To AVIF Format is very slowly and Huge CPU/Memory Usage, so NOT RECOMMENDED!");
                    lines.Add("=".PadRight(NormallyMessageWidth, '='));
                    ShowMessage(string.Join(Environment.NewLine, lines), "Usage", width: 640);
                }
            }
        }

        private void ReduceToQuality_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            try
            {
                Dispatcher.InvokeAsync(() =>
                {
                    if (IsLoaded)
                    {
                        var quality = Convert.ToInt32(ReduceToQuality.Value);
                        ReduceToQuality.ToolTip = $"Reduce Quality: { quality }";
                        ReduceToQualityValue.Text = $"{ quality }";
                        ReduceToQualityValue.ToolTip = ReduceToQuality.ToolTip;
                        ReduceToQualityPanel.ToolTip = ReduceToQuality.ToolTip;
                    }
                });
            }
            catch (Exception ex) { Log(ex.Message); }
        }

        private void ReduceToQuality_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            try
            {
                Dispatcher.InvokeAsync(() =>
                {
                    if (IsLoaded)
                    {
                        var value = ReduceToQuality.Value;
                        var m = value % ReduceToQuality.LargeChange;                        
                        var offset = 0.0;
                        if (e.Delta < 0)
                        {
                            m = m == 0 ? ReduceToQuality.LargeChange : ReduceToQuality.LargeChange - m;
                            offset = value + m;
                        }
                        else if (e.Delta > 0)
                        {
                            m = m == 0 ? ReduceToQuality.LargeChange : m;
                            offset = value - m;
                        }
                        ReduceToQuality.Value = offset;
#if DEBUG
                        Debug.WriteLine($"{e.Delta}, {m}, {offset}");
#endif
                    }
                });
            }
            catch (Exception ex) { Log(ex.Message); }
        }
    }
}
