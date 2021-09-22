using ImageMagick;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;

namespace TouchMeta
{
    public class MetaInfo
    {
        public DateTime? DateAcquired { get; set; } = null;
        public DateTime? DateTaken { get; set; } = null;
        public string Title { get; set; } = null;
        public string Subject { get; set; } = null;
        public string Keywords { get; set; } = null;
        public string Comment { get; set; } = null;
        public string Author { get; set; } = null;
        public string Copyright { get; set; } = null;
    }

    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        private static string AppExec = Application.ResourceAssembly.CodeBase.ToString().Replace("file:///", "").Replace("/", "\\");
        private static string AppPath = Path.GetDirectoryName(AppExec);
        private static string AppName = Path.GetFileNameWithoutExtension(AppPath);
        private static string CachePath =  "cache";
        private static Encoding DBCS = Encoding.GetEncoding("GB18030");
        private static Encoding UTF8 = Encoding.UTF8;
        private static Encoding UNICODE = Encoding.Unicode;

        #region below tags will be touching
        private static string[] tag_date = new string[] {
          "exif:DateTimeDigitized",
          "exif:DateTimeOriginal",
          "exif:DateTime",
          "MicrosoftPhoto:DateAcquired",
          "MicrosoftPhoto:DateTaken",
          //"png:tIME",
          //"tiff:DateTime",
          "xmp:CreateDate",
          //"xmp:DateTimeDigitized",
          //"xmp:DateTimeOriginal",
          "Creation Time",
          "create-date",
          "modify-date",
          "date:create",
          "date:modify",
        };
        private static string[] tag_author = new string[] {
          "exif:Artist",
          "exif:WinXP-Author",
        };
        private static string[] tag_copyright = new string[] {
          "exif:Copyright",
        };
        private static string[] tag_title = new string[] {
          "exif:ImageDescription",
          "exif:WinXP-Title",
        };
        private static string[] tag_subject = new string[] {
          "exif:WinXP-Subject",
        };
        private static string[] tag_comments = new string[] {
          //"exif:WinXP-Comment",
          "exif:WinXP-Comments",
        };
        private static string[] tag_keywords = new string[] {
          "exif:WinXP-Keywords",
        };
        private static string[] tag_rating = new string[] {
          "MicrosoftPhoto:Rating",
        };
        #endregion
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
            catch (Exception ex) { Xceed.Wpf.Toolkit.MessageBox.Show(Application.Current.MainWindow, ex.Message); }
        }

        private static void ClearLog()
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                try { _log_.Clear(); }
                catch (Exception ex) { Xceed.Wpf.Toolkit.MessageBox.Show(Application.Current.MainWindow, ex.Message); }
            }));
        }

        private static void ShowLog()
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                var contents = string.Join(Environment.NewLine, _log_);
                var dlg = new Xceed.Wpf.Toolkit.MessageBox();
                dlg.Language = System.Windows.Markup.XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag);
                dlg.FontFamily = Application.Current.FindResource("MonoSpaceFamily") as FontFamily;
                dlg.Text = contents;
                dlg.Caption = "Metadata Info";
                dlg.MaxWidth = 640;
                dlg.MaxHeight = 480;
                dlg.MouseDoubleClick += (o, e) => { Clipboard.SetText(dlg.Text.Replace("\0", string.Empty).TrimEnd('\0')); };
                dlg.ShowDialog();
            }));
        }

        private static void ShowMessage(string text)
        {
            Xceed.Wpf.Toolkit.MessageBox.Show(Application.Current.MainWindow, text);
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

        private static string BytesToUnicode(string text)
        {
            var result = text;
            if (!string.IsNullOrEmpty(text))
            {
                foreach (Match m in Regex.Matches(text, @"((\d{1,3}, ?){2,}\d{1,3})"))
                {
                    List<byte> bytes = new List<byte>();
                    var values = m.Groups[1].Value.Split(',').Select(s => s.Trim()).ToList();
                    foreach (var value in values)
                    {
                        if (int.Parse(value) > 255) continue;
                        bytes.Add(byte.Parse(value));
                    }
                    if (bytes.Count > 0) result = result.Replace(m.Groups[1].Value, Encoding.Unicode.GetString(bytes.ToArray()));//.TrimEnd('\0'));
                }
            }
            return (result);
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

        public static void ClearMeta(string file)
        {
            if (File.Exists(file))
            {
                var fi = new FileInfo(file);
                var dc = fi.CreationTime;
                var dm = fi.LastWriteTime;
                var da = fi.LastAccessTime;

                using (MagickImage image = new MagickImage(fi.FullName))
                {
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

                    image.Write(fi.FullName);
                }
                fi.CreationTime = dc;
                fi.LastWriteTime = dm;
                fi.LastAccessTime = da;
            }
        }

        public static void TouchMeta(string file, bool force = false, DateTime? dtc = null, DateTime? dtm = null, DateTime? dta = null, MetaInfo meta = null)
        {
            if (File.Exists(file))
            {
                var fi = new FileInfo(file);

                var title = meta is MetaInfo ? meta.Title ?? Path.GetFileNameWithoutExtension(fi.Name) : Path.GetFileNameWithoutExtension(fi.Name);
                var subject = meta is MetaInfo ? meta.Subject : title;
                var author = meta is MetaInfo ? meta.Author : string.Empty;
                var copyright = meta is MetaInfo ? meta.Copyright : author;
                var keywords = meta is MetaInfo ? meta.Keywords : string.Empty;
                var comment = meta is MetaInfo ? meta.Comment : string.Empty;
                if (!string.IsNullOrEmpty(title)) title.Replace("\0", string.Empty).TrimEnd('\0');
                if (!string.IsNullOrEmpty(subject)) subject.Replace("\0", string.Empty).TrimEnd('\0');
                if (!string.IsNullOrEmpty(author)) author.Replace("\0", string.Empty).TrimEnd('\0');
                if (!string.IsNullOrEmpty(copyright)) copyright.Replace("\0", string.Empty).TrimEnd('\0');
                if (!string.IsNullOrEmpty(keywords)) keywords.Replace("\0", string.Empty).TrimEnd('\0');
                if (!string.IsNullOrEmpty(comment)) comment.Replace("\0", string.Empty).TrimEnd('\0');

                using (MagickImage image = new MagickImage(fi.FullName))
                {
                    bool is_png = image.FormatInfo.MimeType.Equals("image/png", StringComparison.CurrentCultureIgnoreCase);
                    bool is_jpg = image.FormatInfo.MimeType.Equals("image/jpeg", StringComparison.CurrentCultureIgnoreCase);

                    var exif = image.HasProfile("exif") ? image.GetExifProfile() : new ExifProfile();
                    var xmp = image.HasProfile("xmp") ? image.GetXmpProfile() : null;

                    #region touch date
                    var dc = dtc ?? fi.CreationTime;
                    var dm = dtm ?? fi.LastWriteTime;
                    var da = dta ?? fi.LastAccessTime;

                    if (!force)
                    {
                        foreach (var tag in tag_date)
                        {
                            if (image.AttributeNames.Contains(tag))
                            {
                                DateTime dv;
                                if (DateTime.TryParse(image.GetAttribute(tag), out dv))
                                {
                                    dc = dv;
                                    dm = dv;
                                    da = dv;
                                    break;
                                }
                            }
                        }
                    }

                    // 2021:09:13 11:00:16
                    var dc_exif = dc.ToString("yyyy:MM:dd HH:mm:ss");
                    var dm_exif = dm.ToString("yyyy:MM:dd HH:mm:ss");
                    var da_exif = da.ToString("yyyy:MM:dd HH:mm:ss");
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

                    foreach (var tag in tag_date)
                    {
                        try
                        {
                            //Log(tag);
                            if (force || !image.AttributeNames.Contains(tag))
                            {
                                var value_old = image.GetAttribute(tag);
                                if (tag.StartsWith("date")) image.SetAttribute(tag, dm_date);
                                else if (tag.StartsWith("exif")) image.SetAttribute(tag, dm_exif);
                                else if (tag.StartsWith("png")) { image.RemoveAttribute(tag); image.SetAttribute(tag, dm_png); }
                                else if (tag.StartsWith("tiff")) { image.RemoveAttribute(tag); image.SetAttribute(tag, dm_date); }
                                else if (tag.StartsWith("Microsoft")) { image.RemoveAttribute(tag); image.SetAttribute(tag, dm_ms); }
                                else if (tag.StartsWith("xmp")) image.SetAttribute(tag, dm_date);
                                else image.SetAttribute(tag, dm_misc);

                                if (is_jpg)
                                {
                                    if (tag.StartsWith("exif") && tag.Substring(5).Equals("DateTime"))
                                        exif.SetValue(ExifTag.DateTime, dm_exif);
                                    else if (tag.StartsWith("exif") && tag.Substring(5).Equals("DateTimeDigitized"))
                                        exif.SetValue(ExifTag.DateTimeDigitized, dm_exif);
                                    else if (tag.StartsWith("exif") && tag.Substring(5).Equals("DateTimeOriginal"))
                                        exif.SetValue(ExifTag.DateTimeOriginal, dm_exif);
                                }
                                Log($"{$"  {tag}".PadRight(32)}= {(value_old == null ? "NULL" : value_old)} => {image.GetAttribute(tag)}");
                            }
                        }
                        catch (Exception ex) { Log(ex.Message); }
                    }
                    #endregion
                    #region touch title
                    foreach (var tag in tag_title)
                    {
                        try
                        {
                            if (force || (!image.AttributeNames.Contains(tag) && !string.IsNullOrEmpty(title)))
                            {
                                var value_old = tag.Contains("WinXP") ? BytesToUnicode(image.GetAttribute(tag)) : image.GetAttribute(tag);
                                if (tag.StartsWith("exif"))
                                {
                                    if (tag.Contains("WinXP")) image.SetAttribute(tag, UnicodeToBytes(title));
                                    else image.SetAttribute(tag, title);
                                }
                                else if (tag.StartsWith("png")) image.SetAttribute(tag, title);
                                else if (tag.StartsWith("Microsoft")) image.SetAttribute(tag, title);
                                if (is_jpg)
                                {
                                    if (tag.StartsWith("exif") && tag.Substring(5).Equals("WinXP-Title"))
                                        exif.SetValue(ExifTag.XPTitle, Encoding.Unicode.GetBytes(title));
                                    else if (tag.StartsWith("exif") && tag.Substring(5).Equals("ImageDescription"))
                                        exif.SetValue(ExifTag.ImageDescription, title);
                                }
                                Log($"{$"  {tag}".PadRight(32)}= {(value_old == null ? "NULL" : value_old)} => {(tag.Contains("WinXP") ? BytesToUnicode(image.GetAttribute(tag)) : image.GetAttribute(tag))}");
                            }
                        }
                        catch (Exception ex) { Log(ex.Message); }
                    }
                    #endregion
                    #region touch subject
                    foreach (var tag in tag_subject)
                    {
                        try
                        {
                            if (force || (!image.AttributeNames.Contains(tag) && !string.IsNullOrEmpty(subject)))
                            {
                                var value_old = tag.Contains("WinXP") ? BytesToUnicode(image.GetAttribute(tag)) : image.GetAttribute(tag);
                                var value_new = string.Empty;
                                if (tag.StartsWith("exif"))
                                {
                                    if (tag.Contains("WinXP")) image.SetAttribute(tag, UnicodeToBytes(subject));
                                    else image.SetAttribute(tag, subject);
                                }
                                else if (tag.StartsWith("png")) image.SetAttribute(tag, subject);
                                else if (tag.StartsWith("Microsoft")) image.SetAttribute(tag, subject);
                                if (is_jpg)
                                {
                                    if (tag.StartsWith("exif") && tag.Substring(5).Equals("WinXP-Subject"))
                                    {
                                        //value_new = 
                                        exif.SetValue(ExifTag.XPSubject, Encoding.Unicode.GetBytes(subject));
                                    }
                                }
                                Log($"{$"  {tag}".PadRight(32)}= {(value_old == null ? "NULL" : value_old)} => {(tag.Contains("WinXP") ? BytesToUnicode(image.GetAttribute(tag)) : image.GetAttribute(tag))}");
                            }
                        }
                        catch (Exception ex) { Log(ex.Message); }
                    }
                    #endregion
                    #region touch author
                    foreach (var tag in tag_author)
                    {
                        try
                        {
                            if (force || (!image.AttributeNames.Contains(tag) && !string.IsNullOrEmpty(author)))
                            {
                                var value_old = tag.Contains("WinXP") ? BytesToUnicode(image.GetAttribute(tag)) : image.GetAttribute(tag);
                                var value_new = string.Empty;
                                if (tag.StartsWith("exif"))
                                {
                                    if (tag.Contains("WinXP")) image.SetAttribute(tag, UnicodeToBytes(author));
                                    else image.SetAttribute(tag, author);
                                }
                                else if (tag.StartsWith("png")) image.SetAttribute(tag, author);
                                else if (tag.StartsWith("Microsoft")) image.SetAttribute(tag, author);
                                if (is_jpg)
                                {
                                    if (tag.StartsWith("exif") && tag.Substring(5).Equals("WinXP-Author"))
                                    {
                                        exif.SetValue(ExifTag.XPAuthor, Encoding.Unicode.GetBytes(author));
                                    }
                                }
                                Log($"{$"  {tag}".PadRight(32)}= {(value_old == null ? "NULL" : value_old)} => {(tag.Contains("WinXP") ? BytesToUnicode(image.GetAttribute(tag)) : image.GetAttribute(tag))}");
                            }
                        }
                        catch (Exception ex) { Log(ex.Message); }
                    }
                    #endregion
                    #region touch copywright
                    foreach (var tag in tag_copyright)
                    {
                        try
                        {
                            if (force || (!image.AttributeNames.Contains(tag) && !string.IsNullOrEmpty(copyright)))
                            {
                                var value_old = tag.Contains("WinXP") ? BytesToUnicode(image.GetAttribute(tag)) : image.GetAttribute(tag);
                                var value_new = string.Empty;
                                if (tag.StartsWith("exif"))
                                {
                                    if (tag.Contains("WinXP")) image.SetAttribute(tag, UnicodeToBytes(copyright));
                                    else image.SetAttribute(tag, copyright);
                                }
                                else if (tag.StartsWith("png")) image.SetAttribute(tag, copyright);
                                else if (tag.StartsWith("Microsoft")) image.SetAttribute(tag, copyright);
                                if (is_jpg)
                                {
                                    if (tag.StartsWith("exif") && tag.Substring(5).Equals("Copyright"))
                                    {
                                        exif.SetValue(ExifTag.Copyright, copyright);
                                    }
                                }
                                Log($"{$"  {tag}".PadRight(32)}= {(value_old == null ? "NULL" : value_old)} => {(tag.Contains("WinXP") ? BytesToUnicode(image.GetAttribute(tag)) : image.GetAttribute(tag))}");
                            }
                        }
                        catch (Exception ex) { Log(ex.Message); }
                    }
                    #endregion
                    #region touch comment
                    foreach (var tag in tag_comments)
                    {
                        try
                        {
                            if (force || (!image.AttributeNames.Contains(tag) && !string.IsNullOrEmpty(comment)))
                            {
                                var value_old = tag.Contains("WinXP") ? BytesToUnicode(image.GetAttribute(tag)) : image.GetAttribute(tag);
                                if (tag.StartsWith("exif"))
                                {
                                    if (tag.Contains("WinXP")) image.SetAttribute(tag, UnicodeToBytes(comment));
                                    else image.SetAttribute(tag, comment);
                                }
                                else if (tag.StartsWith("png")) image.SetAttribute(tag, comment);
                                else if (tag.StartsWith("Microsoft")) image.SetAttribute(tag, comment);
                                if (is_jpg)
                                {
                                    if (tag.StartsWith("exif") && tag.Substring(5).Equals("WinXP-Comment"))
                                        exif.SetValue(ExifTag.XPComment, Encoding.Unicode.GetBytes(comment));
                                    else if (tag.StartsWith("exif") && tag.Substring(5).Equals("WinXP-Comments"))
                                        exif.SetValue(ExifTag.XPComment, Encoding.Unicode.GetBytes(comment));
                                }
                                Log($"{$"  {tag}".PadRight(32)}= {(value_old == null ? "NULL" : value_old)} => {(tag.Contains("WinXP") ? BytesToUnicode(image.GetAttribute(tag)) : image.GetAttribute(tag))}");
                            }
                        }
                        catch (Exception ex) { Log(ex.Message); }
                    }
                    #endregion
                    #region touch keywords
                    foreach (var tag in tag_keywords)
                    {
                        if (force || (!image.AttributeNames.Contains(tag) && !string.IsNullOrEmpty(keywords)))
                        {
                            var value_old = tag.Contains("WinXP") ? BytesToUnicode(image.GetAttribute(tag)) : image.GetAttribute(tag);
                            if (tag.StartsWith("exif"))
                            {
                                if (tag.Contains("WinXP")) image.SetAttribute(tag, UnicodeToBytes(keywords));
                                else image.SetAttribute(tag, keywords);
                                if (tag.Substring(5).Equals("WinXP-Comment"))
                                    exif.SetValue(ExifTag.XPComment, Encoding.Unicode.GetBytes(keywords));
                            }
                            else if (tag.StartsWith("png")) image.SetAttribute(tag, keywords);
                            else if (tag.StartsWith("Microsoft")) image.SetAttribute(tag, keywords);
                            if (is_jpg)
                            {
                                if (tag.StartsWith("exif") && tag.Substring(5).Equals("WinXP-Keywords"))
                                    exif.SetValue(ExifTag.XPKeywords, Encoding.Unicode.GetBytes(keywords));
                            }
                            Log($"{$"  {tag}".PadRight(32)}= {value_old} => {(tag.Contains("WinXP") ? BytesToUnicode(image.GetAttribute(tag)) : image.GetAttribute(tag))}");
                        }
                    }
                    #endregion

                    Log($"{"  Profiles".PadRight(32)}= {string.Join(", ", image.ProfileNames)}");

                    if (exif != null) image.SetProfile(exif);
                    #region touch xmp profile
                    if (xmp != null)
                    {
                        var xml = Encoding.UTF8.GetString(xmp.GetData());

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

                        //Log(xml);
                        xmp = new XmpProfile(Encoding.UTF8.GetBytes(xml));
                        image.SetProfile(xmp);
                    }
                    else
                    {
                        var xml = $"<?xpacket begin='?' id='W5M0MpCehiHzreSzNTczkc9d'?>{Environment.NewLine}<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\"><rdf:Description rdf:about=\"uuid:faf5bdd5-ba3d-11da-ad31-d33d75182f1b\" xmlns:MicrosoftPhoto=\"http://ns.microsoft.com/photo/1.0/\"><MicrosoftPhoto:DateAcquired>{dm_msxmp}</MicrosoftPhoto:DateAcquired></rdf:Description></rdf:RDF></x:xmpmeta>{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}                            <?xpacket end='w'?>";
                        //Log(xml);
                        xmp = new XmpProfile(Encoding.UTF8.GetBytes(xml));
                        image.SetProfile(xmp);
                    }
                    #endregion

                    image.Write(fi.FullName);

                    fi.CreationTime = dc;
                    fi.LastWriteTime = dm;
                    fi.LastAccessTime = da;
                }
            }
        }

        public static void TouchDate(string file, string dt = null, bool force = false, DateTime? dtc = null, DateTime? dtm = null, DateTime? dta = null)
        {
            if (File.Exists(file))
            {
                var fi = new FileInfo(file);
                var dc = dtc ?? fi.CreationTime;
                var dm = dtm ?? fi.LastWriteTime;
                var da = dta ?? fi.LastAccessTime;

                var ov = dm.ToString("yyyy-MM-ddTHH:mm:sszzz");

                if (force)
                {
                    if (fi.CreationTime != dc) fi.CreationTime = dc;
                    if (fi.LastWriteTime != dm) fi.LastWriteTime = dm;
                    if (fi.LastAccessTime != da) fi.LastAccessTime = da;
                    Log($"  Touching Date From {ov} To {dm.ToString("yyyy-MM-ddTHH:mm:sszzz")}");
                }
                else if (string.IsNullOrEmpty(dt))
                {
                    using (var ms = new FileStream(fi.FullName, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        using (MagickImage image = new MagickImage(ms))
                        {
                            bool is_png = image.FormatInfo.MimeType.Equals("image/png");
                            foreach (var tag in tag_date)
                            {
                                if (image.AttributeNames.Contains(tag))
                                {
                                    var v = image.GetAttribute(tag);
                                    var nv = Regex.Replace(v, @"^(\d{4}):(\d{2}):(\d{2})[ |T](.*?)Z?$", "$1-$2-$3T$4");
                                    //Log($"{tag.PadRight(32)}= {v} > {nv}");
                                    dm = DateTime.Parse(tag.Contains("png") ? nv.Substring(0, tag.Length - 1) : nv);
                                    break;
                                }
                            }
                        }
                    }
                    try
                    {
                        fi.CreationTime = dm;
                        fi.LastWriteTime = dm;
                        fi.LastAccessTime = dm;
                        Log($"  Touching Date From {ov} To {dm.ToString("yyyy-MM-ddTHH:mm:sszzz")}");
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
                            Log($"  Touching Date From {ov} To {t.ToString("yyyy-MM-ddTHH:mm:sszzz")}");
                        }
                    }
                    catch (Exception ex) { Log($"{ex.Message}{Environment.NewLine}{ex.StackTrace}"); }
                }
            }
        }

        public static void ShowMeta(string file)
        {
            if (File.Exists(file))
            {
                var fi = new FileInfo(file);
                using (var ms = new FileStream(fi.FullName, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    using (MagickImage image = new MagickImage(ms))
                    {
                        var exif = image.HasProfile("exif") ? image.GetExifProfile() : null;
                        foreach (var attr in image.AttributeNames)
                        {
                            try
                            {
                                var value = image.GetAttribute(attr);
                                if (string.IsNullOrEmpty(value)) continue;
                                if (attr.Contains("WinXP")) value = BytesToUnicode(value);
                                else if (attr.StartsWith("exif", StringComparison.CurrentCultureIgnoreCase) && exif is ExifProfile)
                                {
                                    if (attr.Equals("exif:ImageDescription")) value = exif.GetValue(ExifTag.ImageDescription).Value;
                                    else if (attr.Equals("exif:Copyright")) value = exif.GetValue(ExifTag.Copyright).Value;
                                    else if (attr.Equals("exif:Artist")) value = exif.GetValue(ExifTag.Artist).Value;
                                    else if (attr.Equals("exif:UserComment")) value = UNICODE.GetString(exif.GetValue(ExifTag.UserComment).Value);
                                    else if (attr.Equals("exif:XPAuthor")) value = UNICODE.GetString(exif.GetValue(ExifTag.XPAuthor).Value);
                                    else if (attr.Equals("exif:XPComment")) value = UNICODE.GetString(exif.GetValue(ExifTag.XPComment).Value);
                                    else if (attr.Equals("exif:XPKeywords")) value = UNICODE.GetString(exif.GetValue(ExifTag.XPKeywords).Value);
                                    else if (attr.Equals("exif:XPTitle")) value = UNICODE.GetString(exif.GetValue(ExifTag.XPTitle).Value);
                                    else if (attr.Equals("exif:XPSubject")) value = UNICODE.GetString(exif.GetValue(ExifTag.XPSubject).Value);
                                    if (!string.IsNullOrEmpty(value)) value = value.Replace("\0", string.Empty).TrimEnd('\0');
                                }
                                else if (attr.Equals("png:bKGD")) value = image.BackgroundColor.ToString();
                                else if (attr.Equals("png:cHRM"))
                                {
                                    var cr = XYZ2RGB(image.ChromaRedPrimary.X, image.ChromaRedPrimary.Y, image.ChromaRedPrimary.Z);
                                    var cg = XYZ2RGB(image.ChromaGreenPrimary.X, image.ChromaGreenPrimary.Y, image.ChromaGreenPrimary.Z);
                                    var cb = XYZ2RGB(image.ChromaBluePrimary.X, image.ChromaBluePrimary.Y, image.ChromaBluePrimary.Z);

                                    var r = $"[{image.ChromaRedPrimary.X:F2},{image.ChromaRedPrimary.Y:F2},{image.ChromaRedPrimary.Z:F2}]";
                                    var g = $"[{image.ChromaGreenPrimary.X:F2},{image.ChromaGreenPrimary.Y:F2},{image.ChromaGreenPrimary.Z:F2}]";
                                    var b = $"[{image.ChromaBluePrimary.X:F2},{image.ChromaBluePrimary.Y:F2},{image.ChromaBluePrimary.Z:F2}]";
                                    //value = $"R: {r}{Environment.NewLine}{" ".PadRight(36)}G: {g}{Environment.NewLine}{" ".PadRight(36)}B: {b}";
                                    value = $"R:{cr.ToString()}, G:{cg.ToString()}, B:{cb.ToString()}{Environment.NewLine}XYZ-R: {r}{Environment.NewLine}XYZ-G: {g}{Environment.NewLine}XYZ-B: {b}";
                                }
                                var values = value.Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
                                foreach (var v in values)
                                {
                                    if (v.Length > 64) value = $"{v.Substring(0, 64)} ...";
                                    var text = v.Equals(values.First()) ? $"  {attr.PadRight(32)}= { v }" : $"  {" ".PadRight(34)}{ v }";
                                    Log(text);
                                }
                                //tip.Add(text);
                            }
                            catch (Exception ex) { MessageBox.Show($"{attr} : {ex.Message}"); }
                        }
                        Log($"  {"Profiles".PadRight(32)}= {string.Join(", ", image.ProfileNames)}");
                        foreach (var profile_name in image.ProfileNames)
                        {
                            try
                            {
                                var profile = image.GetProfile(profile_name);
                                var prefix = $"Profile {profile.Name}".PadRight(32, ' ');
                                var bytes = profile.ToByteArray().Select(b => $"{b}");
                                Log($"  {prefix}= {bytes.Count()} bytes");// [{profile.GetType()}]");
                                //if (profile_name.Equals("8bim"))
                                //{
                                //    var _8bim = image.Get8BimProfile();
                                //    Log(_8bim.Values.Count());
                                //    foreach (var v in _8bim.Values) Log($"    0x{v.ID:X2} : {v.ToString()}");
                                //}
                                //Log($"  {prefix}= {bytes.Count()} bytes [{string.Join(", ", bytes)}]");
                            }
                            catch (Exception ex) { Log(ex.Message); }
                        }
                        Log($"  {"Color Space".PadRight(32)}= {Path.GetFileName(image.ColorSpace.ToString())}");
                        Log($"  {"Format Info".PadRight(32)}= {image.FormatInfo.Format.ToString()}, {image.FormatInfo.MimeType}");
                        var xmp = image.HasProfile("xmp") ? image.GetXmpProfile() : null;
                        if (xmp != null)
                        {
                            var xml = Encoding.UTF8.GetString(xmp.GetData());
                            Log($"  {"XMP XML Contents".PadRight(32)}= {xml}");
                        }
                    }
                }
            }
        }

        public DateTime? GetMetaTime(MagickImage image)
        {
            DateTime? result = null;
            if (image is MagickImage)
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
            return (result);
        }

        public DateTime? GetMetaTime(string file)
        {
            DateTime? result = null;
            if (File.Exists(file))
            {
                var fi = new FileInfo(file);
                var dc = fi.CreationTime;
                var dm = fi.LastWriteTime;
                var da = fi.LastAccessTime;

                var ov = dm.ToString("yyyy-MM-ddTHH:mm:sszzz");

                using (var ms = new FileStream(fi.FullName, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    try
                    {
                        using (MagickImage image = new MagickImage(ms))
                        {
                            dm = GetMetaTime(image) ?? dm;
                        }
                    }
                    catch (Exception ex) { ShowMessage(ex.Message); }
                }
                result = dm;
            }
            return (result);
        }

        public MetaInfo GetMetaInfo(MagickImage image)
        {
            MetaInfo result = new MetaInfo();

            if (image is MagickImage)
            {
                var exif = image.HasProfile("exif") ? image.GetExifProfile() : new ExifProfile();
                var xmp = image.HasProfile("xmp") ? image.GetXmpProfile() : null;


                bool is_png = image.FormatInfo.MimeType.Equals("image/png");
                foreach (var tag in tag_date)
                {
                    if (image.AttributeNames.Contains(tag))
                    {
                        var v = image.GetAttribute(tag);
                        var nv = Regex.Replace(v, @"^(\d{4}):(\d{2}):(\d{2})[ |T](.*?)Z?$", "$1-$2-$3T$4");
                        //Log($"{tag.PadRight(32)}= {v} > {nv}");
                        result.DateAcquired = DateTime.Parse(tag.Contains("png") ? nv.Substring(0, tag.Length - 1) : nv);
                        result.DateTaken = result.DateAcquired;
                        break;
                    }
                }
                foreach (var tag in tag_title)
                {
                    if (image.AttributeNames.Contains(tag))
                    {
                        result.Title = tag.Contains("WinXP") ? BytesToUnicode(image.GetAttribute(tag)) : image.GetAttribute(tag);
                        if (tag.Equals("exif:ImageDescription"))
                        {
                            var value = exif.GetValue(ExifTag.ImageDescription);
                            result.Title = value == null ? result.Title : (value.Value ?? result.Title);
                        }
                        else if (tag.Equals("exif:WinXP-Title"))
                        {
                            var value = exif.GetValue(ExifTag.XPTitle);
                            result.Title = value == null ? result.Title : (Encoding.Unicode.GetString(value.Value) ?? result.Title);
                        }
                        if (!string.IsNullOrEmpty(result.Title)) result.Title = result.Title.Replace("\0", string.Empty).TrimEnd('\0');
                        break;
                    }
                }
                foreach (var tag in tag_subject)
                {
                    if (image.AttributeNames.Contains(tag))
                    {
                        if (image.AttributeNames.Contains(tag))
                        {
                            result.Subject = tag.Contains("WinXP") ? BytesToUnicode(image.GetAttribute(tag)) : image.GetAttribute(tag);
                            if (tag.Equals("exif:WinXP-Subject"))
                            {
                                var value = exif.GetValue(ExifTag.XPSubject);
                                result.Subject = value == null ? result.Subject : (Encoding.Unicode.GetString(value.Value) ?? result.Subject);
                            }
                            if (!string.IsNullOrEmpty(result.Subject)) result.Subject = result.Subject.Replace("\0", string.Empty).TrimEnd('\0');
                            break;
                        }
                        break;
                    }
                }
                foreach (var tag in tag_comments)
                {
                    if (image.AttributeNames.Contains(tag))
                    {
                        if (image.AttributeNames.Contains(tag))
                        {
                            result.Comment = tag.Contains("WinXP") ? BytesToUnicode(image.GetAttribute(tag)) : image.GetAttribute(tag);
                            if (tag.Equals("exif:WinXP-Comment"))
                            {
                                var value = exif.GetValue(ExifTag.XPComment);
                                result.Comment = value == null ? result.Comment : (Encoding.Unicode.GetString(value.Value) ?? result.Comment);
                            }
                            if (!string.IsNullOrEmpty(result.Comment)) result.Comment = result.Comment.Replace("\0", string.Empty).TrimEnd('\0');
                            break;
                        }
                    }
                }
                foreach (var tag in tag_keywords)
                {
                    if (image.AttributeNames.Contains(tag))
                    {
                        if (image.AttributeNames.Contains(tag))
                        {
                            result.Keywords = tag.Contains("WinXP") ? BytesToUnicode(image.GetAttribute(tag)) : image.GetAttribute(tag);
                            if (tag.Equals("exif:WinXP-Keywords"))
                            {
                                var value = exif.GetValue(ExifTag.XPKeywords);
                                result.Keywords = value == null ? result.Keywords : (Encoding.Unicode.GetString(value.Value) ?? result.Keywords);
                            }
                            if (!string.IsNullOrEmpty(result.Keywords)) result.Keywords = result.Keywords.Replace("\0", string.Empty).TrimEnd('\0');
                            break;
                        }
                    }
                }
                foreach (var tag in tag_author)
                {
                    if (image.AttributeNames.Contains(tag))
                    {
                        if (image.AttributeNames.Contains(tag))
                        {
                            result.Author = tag.Contains("WinXP") ? BytesToUnicode(image.GetAttribute(tag)) : image.GetAttribute(tag);
                            if (tag.Equals("exif:WinXP-Author"))
                            {
                                var value = exif.GetValue(ExifTag.XPAuthor);
                                result.Author = value == null ? result.Author : (Encoding.Unicode.GetString(value.Value) ?? result.Author);
                            }
                            if (!string.IsNullOrEmpty(result.Author)) result.Author = result.Author.Replace("\0", string.Empty).TrimEnd('\0');
                            break;
                        }
                    }
                }
                foreach (var tag in tag_copyright)
                {
                    if (image.AttributeNames.Contains(tag))
                    {
                        if (image.AttributeNames.Contains(tag))
                        {
                            result.Copyright = tag.Contains("WinXP") ? BytesToUnicode(image.GetAttribute(tag)) : image.GetAttribute(tag);
                            if (tag.Equals("exif:Copyright"))
                            {
                                var value = exif.GetValue(ExifTag.Copyright);
                                result.Copyright = value == null ? result.Copyright : (value.Value ?? result.Copyright);
                            }
                            if (!string.IsNullOrEmpty(result.Copyright)) result.Copyright = result.Copyright.Replace("\0", string.Empty).TrimEnd('\0');
                            break;
                        }
                    }
                }
            }
            return (result);
        }

        public MetaInfo GetMetaInfo(string file)
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
                        using (MagickImage image = new MagickImage(ms))
                        {
                            result = GetMetaInfo(image);
                        }
                    }
                    catch (Exception ex) { ShowMessage(ex.Message); }
                }
            }
            return (result);
        }

        public string ConvertImageTo(string file, MagickFormat fmt)
        {
            var result = file;
            if (File.Exists(file))
            {
                var fi = new FileInfo(file);
                var dc = fi.CreationTime;
                var dm = fi.LastWriteTime;
                var da = fi.LastAccessTime;
                using (var ms = new FileStream(fi.FullName, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
                {
                    try
                    {
                        using (MagickImage image = new MagickImage(ms))
                        {
                            var fmt_info = MagickNET.SupportedFormats.Where(f => f.Format == fmt).FirstOrDefault();
                            var ext = fmt_info is MagickFormatInfo ? fmt_info.Format.ToString() : fmt.ToString();
                            var name = Path.ChangeExtension(fi.FullName, $".{ext.ToLower()}");
                            if (image.Density is Density)
                            {
                                var unit = image.Density.ChangeUnits(DensityUnit.PixelsPerInch);
                                var density = new Density(Math.Round(unit.X), Math.Round(unit.Y), DensityUnit.PixelsPerInch);
                                image.Density = density;
                            }
                            else image.Density = new Density(72, 72, DensityUnit.PixelsPerInch);

                            image.Write(name, fmt);
                            var nfi = new FileInfo(name);
                            nfi.CreationTime = dc;
                            nfi.LastWriteTime = dm;
                            nfi.LastAccessTime = da;
                            var meta = GetMetaInfo(image);
                            Log($"  Convert {file} => {name}");
                            Log("~".PadRight(75, '~'));
                            TouchMeta(name, dtc: dc, dtm: dm, dta: da, meta: meta);
                            result = name;
                        }
                    }
                    catch (Exception ex) { ShowMessage(ex.Message); }
                }
                fi.CreationTime = dc;
                fi.LastWriteTime = dm;
                fi.LastAccessTime = da;
            }
            return (result);
        }

        public static void InitMagicK()
        {
            try
            {
                var magick_cache = Path.IsPathRooted(CachePath) ? CachePath : Path.Combine(AppPath, CachePath);
                //if (!Directory.Exists(magick_cache)) Directory.CreateDirectory(magick_cache);
                if (Directory.Exists(magick_cache)) MagickAnyCPU.CacheDirectory = magick_cache;
                ResourceLimits.Memory = 256 * 1024 * 1024;
                ResourceLimits.LimitMemory(new Percentage(5));
                ResourceLimits.Thread = 4;
                //ResourceLimits.Area = 4096 * 4096;
                //ResourceLimits.Throttle = 
                OpenCL.IsEnabled = true;
                if (Directory.Exists(magick_cache)) OpenCL.SetCacheDirectory(magick_cache);
            }
            catch (Exception ex) { Log(ex.Message); }
        }

        private bool LoadFiles(IEnumerable<string> files)
        {
            var result = false;
            try
            {
                foreach (var file in files) FilesList.Items.Add(file);
                result = true;
            }
            catch (Exception ex) { ShowMessage(ex.Message); }
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
            catch (Exception ex) { ShowMessage(ex.Message); }
            return (result);
        }

        private void UpdateFileTimeInfo(string file = null)
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
            catch (Exception ex) { ShowMessage(ex.Message); }
        }

        public MainWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            Icon = new BitmapImage(new Uri("pack://application:,,,/TouchMeta;component/Resources/exif.ico"));

            InitMagicK();

            #region Default UI values
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
            #endregion

            var args = Environment.GetCommandLineArgs();
            LoadFiles(args.Skip(1).ToArray());
        }

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            var fmts = e.Data.GetFormats(true);
#if DEBUG
            Debug.WriteLine(string.Join(", ", fmts));
#endif
            if (new List<string>(fmts).Contains("FileDrop"))
            {
                e.Effects = DragDropEffects.Link;
            }
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            var fmts = e.Data.GetFormats(true);
            if (new List<string>(fmts).Contains("FileDrop"))
            {
                var files = e.Data.GetData("FileDrop");
                if (files is IEnumerable<string>)
                {
                    LoadFiles((files as IEnumerable<string>).ToArray());
                }
            }
        }

        private void FilesListAction_Click(object sender, RoutedEventArgs e)
        {
            if (sender == GetFileTimeFromSelected)
            {
                if (FilesList.SelectedItem != null)
                {
                    var file = FilesList.SelectedItem as string;
                    if (File.Exists(file))
                    {
                        var fi = new FileInfo(file);

                        DateCreated.SelectedDate = fi.CreationTime;
                        DateModified.SelectedDate = fi.LastWriteTime;
                        DateAccessed.SelectedDate = fi.LastAccessTime;

                        TimeCreated.Value = fi.CreationTime;
                        TimeModified.Value = fi.LastWriteTime;
                        TimeAccessed.Value = fi.LastAccessTime;
                    }
                }
            }
            else if (sender == GetMetaTimeFromSelected)
            {
                if (FilesList.SelectedItem != null)
                {
                    var file = FilesList.SelectedItem as string;
                    var dt = GetMetaTime(file);
                    if (dt != null)
                    {
                        DateCreated.SelectedDate = dt;
                        DateModified.SelectedDate = dt;
                        DateAccessed.SelectedDate = dt;

                        TimeCreated.Value = dt;
                        TimeModified.Value = dt;
                        TimeAccessed.Value = dt;
                    }
                }
            }
            else if (sender == GetMetaInfoFromSelected)
            {
                if (FilesList.SelectedItem != null)
                {
                    var file = FilesList.SelectedItem as string;
                    var meta = GetMetaInfo(file);

                    DateCreated.SelectedDate = meta.DateAcquired ?? meta.DateTaken ?? DateCreated.SelectedDate;
                    DateModified.SelectedDate = meta.DateAcquired ?? meta.DateTaken ?? DateModified.SelectedDate;
                    DateAccessed.SelectedDate = meta.DateAcquired ?? meta.DateTaken ?? DateAccessed.SelectedDate;

                    TimeCreated.Value = meta.DateAcquired ?? meta.DateTaken ?? TimeCreated.Value;
                    TimeModified.Value = meta.DateAcquired ?? meta.DateTaken ?? TimeModified.Value;
                    TimeAccessed.Value = meta.DateAcquired ?? meta.DateTaken ?? TimeAccessed.Value;

                    MetaInputTitleText.Text = meta.Title ?? MetaInputTitleText.Text;
                    MetaInputSubjectText.Text = meta.Subject ?? MetaInputSubjectText.Text;
                    MetaInputCommentText.Text = meta.Comment ?? MetaInputCommentText.Text;
                    MetaInputKeywordsText.Text = meta.Keywords ?? MetaInputKeywordsText.Text;
                    MetaInputAuthorText.Text = meta.Author ?? MetaInputAuthorText.Text;
                    MetaInputCopyrightText.Text = meta.Copyright ?? MetaInputCopyrightText.Text;
                }
            }
            else if (sender == ConvertSelectedToJpg)
            {
                if (FilesList.Items.Count >= 1)
                {
                    ClearLog();
                    List<string> files = new List<string>();
                    Dispatcher.InvokeAsync(() =>
                    {
                        foreach (var item in FilesList.SelectedItems.Count > 0 ? FilesList.SelectedItems : FilesList.Items) files.Add(item as string);
                        foreach (var file in files)
                        {
                            Log($"  {file}");
                            Log("-".PadRight(75, '-'));
                            var ret = ConvertImageTo(file, MagickFormat.Jpg);
                            if (!string.IsNullOrEmpty(ret) && files.IndexOf(ret) < 0 && File.Exists(ret)) FilesList.Items.Add(ret);
                            Log("=".PadRight(75, '='));
                        }
                        ShowLog();
                    });
                }
            }
            else if (sender == ConvertSelectedToPng)
            {
                if (FilesList.Items.Count >= 1)
                {
                    ClearLog();
                    List<string> files = new List<string>();
                    Dispatcher.InvokeAsync(() =>
                    {
                        foreach (var item in FilesList.SelectedItems.Count > 0 ? FilesList.SelectedItems : FilesList.Items) files.Add(item as string);
                        foreach (var file in files)
                        {
                            Log($"  {file}");
                            Log("-".PadRight(75, '-'));
                            var ret = ConvertImageTo(file, MagickFormat.Png);
                            if (!string.IsNullOrEmpty(ret) && files.IndexOf(ret) < 0 && File.Exists(ret)) FilesList.Items.Add(ret);
                            Log("=".PadRight(75, '='));
                        }
                        ShowLog();
                    });
                }
            }
            else if (sender == ConvertSelectedToGif)
            {
                if (FilesList.Items.Count >= 1)
                {
                    ClearLog();
                    List<string> files = new List<string>();
                    Dispatcher.InvokeAsync(() =>
                    {
                        foreach (var item in FilesList.SelectedItems.Count > 0 ? FilesList.SelectedItems : FilesList.Items) files.Add(item as string);
                        foreach (var file in files)
                        {
                            Log($"  {file}");
                            Log("-".PadRight(75, '-'));
                            var ret = ConvertImageTo(file, MagickFormat.Gif);
                            if (!string.IsNullOrEmpty(ret) && files.IndexOf(ret) < 0 && File.Exists(ret)) FilesList.Items.Add(ret);
                            Log("=".PadRight(75, '='));
                        }
                        ShowLog();
                    });
                }
            }
            else if (sender == ConvertSelectedToWebp)
            {
                if (FilesList.Items.Count >= 1)
                {
                    ClearLog();
                    List<string> files = new List<string>();
                    Dispatcher.InvokeAsync(() =>
                    {
                        foreach (var item in FilesList.SelectedItems.Count > 0 ? FilesList.SelectedItems : FilesList.Items) files.Add(item as string);
                        foreach (var file in files)
                        {
                            Log($"  {file}");
                            Log("-".PadRight(75, '-'));
                            var ret = ConvertImageTo(file, MagickFormat.WebP);
                            if (!string.IsNullOrEmpty(ret) && files.IndexOf(ret) < 0 && File.Exists(ret)) FilesList.Items.Add(ret);
                            Log("=".PadRight(75, '='));
                        }
                        ShowLog();
                    });
                }
            }
            else if (sender == RemoveSelected)
            {
                try
                {
                    foreach (var i in FilesList.SelectedItems.OfType<string>().ToList())
                        FilesList.Items.Remove(i);
                }
                catch (Exception ex) { Xceed.Wpf.Toolkit.MessageBox.Show(Application.Current.MainWindow, ex.Message); }
            }
            else if (sender == RemoveAll)
            {
                FilesList.Items.Clear();
            }
        }

        private void FilesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FilesList.SelectedItem != null)
            {
                var file = FilesList.SelectedItem as string;
                UpdateFileTimeInfo(file);
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
                    var d_value = DateCreated.SelectedDate ?? TimeCreated.Value ?? DateTime.Now;
                    var t_value = TimeCreated.Value ?? DateCreated.SelectedDate ?? DateTime.Now;
                    DateCreated.SelectedDate = d_value.Date + t_value.TimeOfDay;
                    TimeCreated.Value = DateCreated.SelectedDate;
                }
                else if (sender == DateModified)
                {
                    var d_value = DateModified.SelectedDate ?? TimeModified.Value ?? DateTime.Now;
                    var t_value = TimeModified.Value ?? DateModified.SelectedDate ?? DateTime.Now;
                    DateModified.SelectedDate = d_value.Date + t_value.TimeOfDay;
                    TimeModified.Value = DateModified.SelectedDate;
                }
                else if (sender == DateAccessed)
                {
                    var d_value = DateAccessed.SelectedDate ?? TimeAccessed.Value ?? DateTime.Now;
                    var t_value = TimeAccessed.Value ?? DateAccessed.SelectedDate ?? DateTime.Now;
                    DateAccessed.SelectedDate = d_value.Date + t_value.TimeOfDay;
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
                    var d_value = DateCreated.SelectedDate ?? TimeCreated.Value ?? DateTime.Now;
                    var t_value = TimeCreated.Value ?? DateCreated.SelectedDate ?? DateTime.Now;
                    DateCreated.SelectedDate = d_value.Date + t_value.TimeOfDay;
                    TimeCreated.Value = DateCreated.SelectedDate;
                }
                else if (sender == TimeModified)
                {
                    var d_value = DateModified.SelectedDate ?? TimeModified.Value ?? DateTime.Now;
                    var t_value = TimeModified.Value ?? DateModified.SelectedDate ?? DateTime.Now;
                    DateModified.SelectedDate = d_value.Date + t_value.TimeOfDay;
                    TimeModified.Value = DateModified.SelectedDate;
                }
                else if (sender == TimeAccessed)
                {
                    var d_value = DateAccessed.SelectedDate ?? TimeAccessed.Value ?? DateTime.Now;
                    var t_value = TimeAccessed.Value ?? DateAccessed.SelectedDate ?? DateTime.Now;
                    DateAccessed.SelectedDate = d_value.Date + t_value.TimeOfDay;
                    TimeAccessed.Value = DateAccessed.SelectedDate;
                }
                UpdateFileTimeInfo();
                _time_changed_ = false;
            }
        }

        private void BtnAction_Click(object sender, RoutedEventArgs e)
        {
            if (sender == SetCreatedDateTimeToAll)
            {
                DateModified.SelectedDate = DateCreated.SelectedDate;
                DateAccessed.SelectedDate = DateCreated.SelectedDate;
                TimeModified.Value = DateCreated.SelectedDate;
                TimeAccessed.Value = DateCreated.SelectedDate;
            }
            else if (sender == SetModifiedDateTimeToAll)
            {
                DateCreated.SelectedDate = DateModified.SelectedDate;
                DateAccessed.SelectedDate = DateModified.SelectedDate;
                TimeCreated.Value = DateModified.SelectedDate;
                TimeAccessed.Value = DateModified.SelectedDate;
            }
            else if (sender == SetAccessedDateTimeToAll)
            {
                DateCreated.SelectedDate = DateAccessed.SelectedDate;
                DateModified.SelectedDate = DateAccessed.SelectedDate;
                TimeCreated.Value = DateAccessed.SelectedDate;
                TimeModified.Value = DateAccessed.SelectedDate;
            }
            else if (sender == ShowMetaInputPopup)
            {
                if (MetaInputPopup.StaysOpen)
                    MetaInputPopup.IsOpen = !MetaInputPopup.IsOpen;
                else
                    MetaInputPopup.IsOpen = true;
            }
            else if (sender == FileTimeImport)
            {
                var dt = DateTime.Now;
                if (DateTime.TryParse(FileTimeImportText.Text, out dt))
                {
                    DateCreated.SelectedDate = dt;
                    DateModified.SelectedDate = dt;
                    DateAccessed.SelectedDate = dt;

                    TimeCreated.Value = dt;
                    TimeModified.Value = dt;
                    TimeAccessed.Value = dt;

                    UpdateFileTimeInfo();
                }
            }
            else if (sender == BtnTouchTime)
            {
                if (FilesList.Items.Count >= 1)
                {
                    ClearLog();
                    List<string> files = new List<string>();
                    Dispatcher.InvokeAsync(() =>
                    {
                        foreach (var item in FilesList.SelectedItems.Count > 0 ? FilesList.SelectedItems : FilesList.Items) files.Add(item as string);
                        foreach (var file in files)
                        {
                            Log($"  {file}");
                            Log("-".PadRight(75, '-'));
                            if (Keyboard.Modifiers == ModifierKeys.Control)
                                TouchDate(file, force: true, dtc: DateCreated.SelectedDate, dtm: DateModified.SelectedDate, dta: DateAccessed.SelectedDate);
                            else TouchDate(file);
                            Log("=".PadRight(75, '='));
                        }
                        ShowLog();
                    });
                }
            }
            else if (sender == BtnTouchMeta)
            {
                if (FilesList.Items.Count >= 1)
                {
                    ClearLog();
                    List<string> files = new List<string>();
                    Dispatcher.InvokeAsync(() =>
                    {
                        var force = Keyboard.Modifiers == ModifierKeys.Control;
                        var dtc = DateCreated.SelectedDate;
                        var dtm = DateModified.SelectedDate;
                        var dta = DateAccessed.SelectedDate;
                        var meta = new MetaInfo()
                        {
                            Title = MetaInputTitleText.Text,
                            Subject = MetaInputSubjectText.Text,
                            Comment = MetaInputCommentText.Text,
                            Keywords = MetaInputKeywordsText.Text,
                            Author = MetaInputAuthorText.Text,
                            Copyright = MetaInputCopyrightText.Text
                        };

                        foreach (var item in FilesList.SelectedItems.Count > 0 ? FilesList.SelectedItems : FilesList.Items) files.Add(item as string);
                        foreach (var file in files)
                        {
                            Log($"  {file}");
                            Log("-".PadRight(75, '-'));
                            TouchMeta(file, force: force, dtc: dtc, dtm: dtm, dta: dta, meta: meta);
                            Log("=".PadRight(75, '='));
                        }
                        ShowLog();
                    });
                }
            }
            else if (sender == BtnClearMeta)
            {
                if (FilesList.Items.Count >= 1)
                {
                    ClearLog();
                    List<string> files = new List<string>();
                    Dispatcher.InvokeAsync(() =>
                    {
                        foreach (var item in FilesList.SelectedItems.Count > 0 ? FilesList.SelectedItems : FilesList.Items) files.Add(item as string);
                        foreach (var file in files)
                        {
                            Log($"  {file}");
                            Log("-".PadRight(75, '-'));
                            ClearMeta(file);
                            Log("=".PadRight(75, '='));
                        }
                        ShowLog();
                    });
                }
            }
            else if (sender == BtnShowMeta)
            {
                if (FilesList.Items.Count >= 1)
                {
                    ClearLog();
                    List<string> files = new List<string>();
                    Dispatcher.InvokeAsync(() =>
                    {
                        foreach (var item in FilesList.SelectedItems.Count > 0 ? FilesList.SelectedItems : FilesList.Items) files.Add(item as string);
                        foreach (var file in files)
                        {
                            Log($"  {file}");
                            Log("-".PadRight(75, '-'));
                            ShowMeta(file);
                            Log("=".PadRight(75, '='));
                        }
                        ShowLog();
                    });
                }
            }
            else if (sender == BtnOpenFile)
            {
                LoadFiles();
            }

        }

    }
}
