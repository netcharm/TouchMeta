using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;

using ImageMagick;
using System.Xml;

namespace NetChamr
{
    public class MetaInfo
    {
        public DateTime? DateCreated { get; set; } = null;
        public DateTime? DateModified { get; set; } = null;
        public DateTime? DateAccesed { get; set; } = null;

        public DateTime? DateAcquired { get; set; } = null;
        public DateTime? DateTaken { get; set; } = null;
        public string Title { get; set; } = null;
        public string Subject { get; set; } = null;
        public string Keywords { get; set; } = null;
        public string Comment { get; set; } = null;
        public string Author { get; set; } = null;
        public string Copyright { get; set; } = null;
        public Dictionary<string, string> Attributes { get; set; } = null;
        public Dictionary<string, IImageProfile> Profiles { get; set; } = null;
    }

    class Metadata
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
          "exif:WinXP-Title",
        };
        private static string[] tag_subject = new string[] {
          "exif:WinXP-Subject",
        };
        private static string[] tag_comments = new string[] {
          "exif:ImageDescription",
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
        private static void Log(string text)
        {
#if DEBUG
            Debug.WriteLine(text);
            Console.WriteLine(text);
#else
            Console.WriteLine(text);
#endif
        }

        private static void ShowMessage(string text)
        {
            Log(text);
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

        private static string FormatXML(string xml)
        {
            var result = xml;
            try
            {
                var doc = new XmlDocument();
                doc.LoadXml(xml);
                result = FormatXML(doc);
            }
            catch (Exception ex) { ShowMessage(ex.Message); }
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

                    #region touch attributes and profiles
                    if (meta.Attributes != null && meta.Attributes.Count > 0)
                    {
                        foreach (var kv in meta.Attributes)
                        {
                            try
                            {
                                var attr = kv.Key;
                                var value = kv.Value;
                                if (force || !image.AttributeNames.Contains(attr)) image.SetAttribute(attr, value);
                            }
                            catch { }
                        }
                    }
                    if (meta.Profiles != null && meta.Profiles.Count > 0)
                    {
                        foreach (var kv in meta.Profiles)
                        {
                            try
                            {
                                var profile_name = kv.Key;
                                var profile = kv.Value;
                                if (force || !image.HasProfile(profile_name)) image.SetProfile(profile);
                            }
                            catch { }
                        }
                    }
                    #endregion

                    var exif = image.HasProfile("exif") ? image.GetExifProfile() : new ExifProfile();
                    var xmp = image.HasProfile("xmp") ? image.GetXmpProfile() : null;

                    #region touch date
                    var dc = dtc ?? (meta is MetaInfo ? meta.DateCreated : null) ?? fi.CreationTime;
                    var dm = dtm ?? (meta is MetaInfo ? meta.DateModified : null) ?? fi.LastWriteTime;
                    var da = dta ?? (meta is MetaInfo ? meta.DateAccesed : null) ?? fi.LastAccessTime;

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
                                Log($"{$"{tag}".PadRight(32)}= {(value_old == null ? "NULL" : value_old)} => {image.GetAttribute(tag)}");
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
                                Log($"{$"{tag}".PadRight(32)}= {(value_old == null ? "NULL" : value_old)} => {(tag.Contains("WinXP") ? BytesToUnicode(image.GetAttribute(tag)) : image.GetAttribute(tag))}");
                            }
                            else
                            {
                                if (tag.Equals("exif:WinXP-Title"))
                                {
                                    title = BytesToUnicode(image.GetAttribute(tag));
                                    if (exif.GetValue(ExifTag.XPTitle) == null)
                                    {
                                        if (!string.IsNullOrEmpty(title)) exif.SetValue(ExifTag.XPTitle, Encoding.Unicode.GetBytes(title));
                                    }
                                    else title = Encoding.Unicode.GetString(exif.GetValue(ExifTag.XPTitle).Value);
                                }
                                else if (tag.Equals("exif:ImageDescription"))
                                {
                                    title = image.GetAttribute(tag);
                                    if (exif.GetValue(ExifTag.ImageDescription) == null)
                                    {
                                        if (!string.IsNullOrEmpty(title)) exif.SetValue(ExifTag.ImageDescription, title);
                                    }
                                    else title = exif.GetValue(ExifTag.ImageDescription).Value;
                                }
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
                                Log($"{$"{tag}".PadRight(32)}= {(value_old == null ? "NULL" : value_old)} => {(tag.Contains("WinXP") ? BytesToUnicode(image.GetAttribute(tag)) : image.GetAttribute(tag))}");
                            }
                            else
                            {
                                if (tag.Equals("exif:WinXP-Subject"))
                                {
                                    subject = BytesToUnicode(image.GetAttribute(tag));
                                    if (exif.GetValue(ExifTag.XPSubject) == null)
                                    {
                                        if (!string.IsNullOrEmpty(subject)) exif.SetValue(ExifTag.XPSubject, Encoding.Unicode.GetBytes(subject));
                                    }
                                    else subject = Encoding.Unicode.GetString(exif.GetValue(ExifTag.XPSubject).Value);
                                }
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
                                    if (tag.Equals("exif:Artist")) exif.SetValue(ExifTag.Artist, author);
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
                                Log($"{$"{tag}".PadRight(32)}= {(value_old == null ? "NULL" : value_old)} => {(tag.Contains("WinXP") ? BytesToUnicode(image.GetAttribute(tag)) : image.GetAttribute(tag))}");
                            }
                            else
                            {
                                if (tag.Equals("exif:WinXP-Author"))
                                {
                                    author = image.GetAttribute(tag);
                                    if (exif.GetValue(ExifTag.XPAuthor) == null)
                                    {
                                        if (!string.IsNullOrEmpty(author)) exif.SetValue(ExifTag.XPAuthor, Encoding.Unicode.GetBytes(author));
                                    }
                                    else author = Encoding.Unicode.GetString(exif.GetValue(ExifTag.XPAuthor).Value);
                                }
                                else if (tag.Equals("exif:Artist"))
                                {
                                    author = image.GetAttribute(tag);
                                    if (exif.GetValue(ExifTag.Artist) == null)
                                    {
                                        if (!string.IsNullOrEmpty(author)) exif.SetValue(ExifTag.Artist, author);
                                    }
                                    else author = exif.GetValue(ExifTag.Artist).Value;
                                }
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
                                Log($"{$"{tag}".PadRight(32)}= {(value_old == null ? "NULL" : value_old)} => {(tag.Contains("WinXP") ? BytesToUnicode(image.GetAttribute(tag)) : image.GetAttribute(tag))}");
                            }
                            else
                            {
                                if (tag.Equals("exif:Copyright"))
                                {
                                    copyright = image.GetAttribute(tag);
                                    if (exif.GetValue(ExifTag.Copyright) == null)
                                    {
                                        if (!string.IsNullOrEmpty(copyright)) exif.SetValue(ExifTag.Copyright, copyright);
                                    }
                                    else copyright = exif.GetValue(ExifTag.Copyright).Value;
                                }
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
                                Log($"{$"{tag}".PadRight(32)}= {(value_old == null ? "NULL" : value_old)} => {(tag.Contains("WinXP") ? BytesToUnicode(image.GetAttribute(tag)) : image.GetAttribute(tag))}");
                            }
                            else
                            {
                                if (tag.Equals("exif:WinXP-Comment"))
                                {
                                    comment = image.GetAttribute(tag);
                                    if (exif.GetValue(ExifTag.XPComment) == null)
                                    {
                                        if (!string.IsNullOrEmpty(comment)) exif.SetValue(ExifTag.XPComment, Encoding.Unicode.GetBytes(comment));
                                    }
                                    else comment = Encoding.Unicode.GetString(exif.GetValue(ExifTag.XPComment).Value);
                                }
                                else if (tag.Equals("exif:WinXP-Comments"))
                                {
                                    comment = image.GetAttribute(tag);
                                    if (exif.GetValue(ExifTag.XPComment) == null)
                                    {
                                        if (!string.IsNullOrEmpty(comment)) exif.SetValue(ExifTag.XPComment, Encoding.Unicode.GetBytes(comment));
                                    }
                                    else comment = Encoding.Unicode.GetString(exif.GetValue(ExifTag.XPComment).Value);
                                }
                            }
                        }
                        catch (Exception ex) { Log(ex.Message); }
                    }
                    #endregion
                    #region touch keywords
                    foreach (var tag in tag_keywords)
                    {
                        try
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
                                Log($"{$"{tag}".PadRight(32)}= {value_old} => {(tag.Contains("WinXP") ? BytesToUnicode(image.GetAttribute(tag)) : image.GetAttribute(tag))}");
                            }
                            else
                            {
                                if (tag.Equals("exif:WinXP-Keywords"))
                                {
                                    keywords = image.GetAttribute(tag);
                                    if (exif.GetValue(ExifTag.XPKeywords) == null)
                                    {
                                        if (!string.IsNullOrEmpty(keywords)) exif.SetValue(ExifTag.XPKeywords, Encoding.Unicode.GetBytes(keywords));
                                    }
                                    else keywords = Encoding.Unicode.GetString(exif.GetValue(ExifTag.XPKeywords).Value);
                                }
                            }
                        }
                        catch (Exception ex) { Log(ex.Message); }
                    }
                    #endregion

                    Log($"{"  Profiles".PadRight(32)}= {string.Join(", ", image.ProfileNames)}");

                    if (exif != null) image.SetProfile(exif);
                    #region touch xmp profile
                    if (xmp == null)
                    {
                        //var xml = $"<?xpacket begin='?' id='W5M0MpCehiHzreSzNTczkc9d'?>{Environment.NewLine}<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\"><rdf:Description rdf:about=\"uuid:faf5bdd5-ba3d-11da-ad31-d33d75182f1b\" xmlns:MicrosoftPhoto=\"http://ns.microsoft.com/photo/1.0/\"><MicrosoftPhoto:DateAcquired>{dm_msxmp}</MicrosoftPhoto:DateAcquired></rdf:Description></rdf:RDF></x:xmpmeta>{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}                            <?xpacket end='w'?>";
                        var xml = $"<?xpacket begin='?' id='W5M0MpCehiHzreSzNTczkc9d'?><x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\"><rdf:Description rdf:about=\"uuid:faf5bdd5-ba3d-11da-ad31-d33d75182f1b\" xmlns:MicrosoftPhoto=\"http://ns.microsoft.com/photo/1.0/\"><MicrosoftPhoto:DateAcquired>{dm_msxmp}</MicrosoftPhoto:DateAcquired><MicrosoftPhoto:DateTaken>{dm_msxmp}</MicrosoftPhoto:DateTaken></rdf:Description><rdf:Description about='' xmlns:exif='http://ns.adobe.com/exif/1.0/'><exif:DateTimeDigitized>{dm_ms}</exif:DateTimeDigitized><exif:DateTimeOriginal></exif:DateTimeOriginal></rdf:Description>{dm_ms}</rdf:RDF></x:xmpmeta><?xpacket end='w'?>";

                        xmp = new XmpProfile(Encoding.UTF8.GetBytes(xml));
                        image.SetProfile(xmp);
                    }
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
                                #region Title node
                                if (xml_doc.GetElementsByTagName("dc:title").Count <= 0)
                                {
                                    var desc = xml_doc.CreateElement("rdf:Description", "rdf");
                                    desc.SetAttribute("rdf:about", "");
                                    desc.SetAttribute("xmlns:dc", "http://purl.org/dc/elements/1.1/");
                                    desc.AppendChild(xml_doc.CreateElement("dc:title", "dc"));
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
                                        desc.SetAttribute("xmlns:exif", "http://ns.adobe.com/exif/1.0/");
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
                                        desc.SetAttribute("xmlns:exif", "http://ns.adobe.com/exif/1.0/");
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
                                    desc.SetAttribute("xmlns:tiff", "http://ns.adobe.com/tiff/1.0/");
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
                                        desc.SetAttribute("xmlns:MicrosoftPhoto", "http://ns.microsoft.com/photo/1.0");
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
                                        desc.SetAttribute("xmlns:MicrosoftPhoto", "http://ns.microsoft.com/photo/1.0");
                                        desc.AppendChild(xml_doc.CreateElement("MicrosoftPhoto:DateTaken", "MicrosoftPhoto"));
                                        root_node.AppendChild(desc);
                                    }
                                }
                                #endregion

                                #region xml nodes updating
                                foreach (XmlNode node in xml_doc.GetElementsByTagName("rdf:Description"))
                                {
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
                                    }
                                }
                                #endregion
                                #region pretty xml
                                xml = FormatXML(xml_doc).Replace("\"", "'");
                                foreach (var ns in new string[] { "rdf", "dc", "exif", "tiff", "MicrosoftPhoto" })
                                {
                                    xml = xml.Replace($" xmlns:{ns}='{ns}'", "");
                                }
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

                var ov = dm.ToString("yyyy-MM-ddTHH:mm:sszzz");

                if (force)
                {
                    if (fi.CreationTime != dc) fi.CreationTime = dc;
                    if (fi.LastWriteTime != dm) fi.LastWriteTime = dm;
                    if (fi.LastAccessTime != da) fi.LastAccessTime = da;
                    Log($"Touching Date From {ov} To {dm.ToString("yyyy-MM-ddTHH:mm:sszzz")}");
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
                                if (image.AttributeNames.Contains(tag) && !tag.Equals("date:modify") && !tag.Equals("date:create"))
                                {
                                    var v = image.GetAttribute(tag);
                                    var nv = Regex.Replace(v, @"^(\d{4}):(\d{2}):(\d{2})[ |T](.*?)Z?$", "$1-$2-$3T$4");
                                    //Log($"{tag.PadRight(32)}= {v} > {nv}");
                                    if (DateTime.TryParse(tag.Contains("png") ? nv.Substring(0, tag.Length - 1) : nv, out dm)) break;
                                }
                            }
                        }
                    }
                    try
                    {
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
                        var exif = image.HasProfile("exif") ? image.GetExifProfile() : new ExifProfile();
                        var exif_invalid = exif.InvalidTags;
                        Log($"{"Dimensions".PadRight(32)}= {image.Width}x{image.Height}x{image.Depth * image.ChannelCount}");
                        Log($"{"TotalPixels".PadRight(32)}= {image.Width * image.Height / 1000.0 / 1000.0:F2} MegaPixels");
                        Log($"{"ColorSpace".PadRight(32)}= {image.ColorSpace.ToString()}");
                        Log($"{"ColorType".PadRight(32)}= {image.ColorType.ToString()}");
                        //Log($"{"TotalColors".PadRight(32)}= {image.TotalColors}");
                        Log($"{"FormatInfo".PadRight(32)}= {image.FormatInfo.Format.ToString()}, MIME:{image.FormatInfo.MimeType}");
                        Log($"{"Compression".PadRight(32)}= {image.Compression.ToString()}");
                        Log($"{"Filter".PadRight(32)}= {(image.FilterType == FilterType.Undefined ? "Adaptive" : image.FilterType.ToString())}");
                        Log($"{"Interlace".PadRight(32)}= {image.Interlace.ToString()}");
                        Log($"{"Interpolate".PadRight(32)}= {image.Interpolate.ToString()}");
                        if (image.Density != null)
                        {
                            var is_ppi = image.Density.Units == DensityUnit.PixelsPerInch;
                            var is_ppc = image.Density.Units == DensityUnit.PixelsPerCentimeter;
                            var density = is_ppi ? image.Density : image.Density.ChangeUnits(DensityUnit.PixelsPerInch);
                            var unit = is_ppi ? "PPI" : (is_ppc ? "PPC" : "UNK");
                            if (is_ppi)
                                Log($"{"Resolution/Density".PadRight(32)}= {density.X:F0} PPI x {density.Y:F0} PPI");
                            else
                                Log($"{"Resolution/Density".PadRight(32)}= {density.X:F0} PPI x {density.Y:F0} PPI [{image.Density.X:F2} {unit} x {image.Density.Y:F2} {unit}]");
                        }
                        foreach (var attr in image.AttributeNames)
                        {
                            try
                            {
                                var value = image.GetAttribute(attr);
                                if (string.IsNullOrEmpty(value)) continue;
                                if (attr.Contains("WinXP")) value = BytesToUnicode(value);
                                else if (attr.StartsWith("exif", StringComparison.CurrentCultureIgnoreCase) && exif is ExifProfile)
                                {
                                    if (attr.Equals("exif:ImageDescription"))
                                        value = exif.GetValue(ExifTag.ImageDescription) != null ? exif.GetValue(ExifTag.ImageDescription).Value : image.GetAttribute(attr) ?? string.Empty;
                                    else if (attr.Equals("exif:Copyright"))
                                        value = exif.GetValue(ExifTag.Copyright) != null ? exif.GetValue(ExifTag.Copyright).Value : image.GetAttribute(attr) ?? string.Empty;
                                    else if (attr.Equals("exif:Artist"))
                                        value = exif.GetValue(ExifTag.Artist) != null ? exif.GetValue(ExifTag.Artist).Value : image.GetAttribute(attr) ?? string.Empty;
                                    else if (attr.Equals("exif:UserComment"))
                                        value = exif.GetValue(ExifTag.UserComment) != null ? UNICODE.GetString(exif.GetValue(ExifTag.UserComment).Value) : image.GetAttribute(attr) ?? string.Empty;
                                    else if (attr.Equals("exif:XPAuthor"))
                                        value = exif.GetValue(ExifTag.XPAuthor) != null ? UNICODE.GetString(exif.GetValue(ExifTag.XPAuthor).Value) : image.GetAttribute(attr) ?? string.Empty;
                                    else if (attr.Equals("exif:XPComment"))
                                        value = exif.GetValue(ExifTag.XPComment) != null ? UNICODE.GetString(exif.GetValue(ExifTag.XPComment).Value) : image.GetAttribute(attr) ?? string.Empty;
                                    else if (attr.Equals("exif:XPKeywords"))
                                        value = exif.GetValue(ExifTag.XPKeywords) != null ? UNICODE.GetString(exif.GetValue(ExifTag.XPKeywords).Value) : image.GetAttribute(attr) ?? string.Empty;
                                    else if (attr.Equals("exif:XPTitle"))
                                        value = exif.GetValue(ExifTag.XPTitle) != null ? UNICODE.GetString(exif.GetValue(ExifTag.XPTitle).Value) : image.GetAttribute(attr) ?? string.Empty;
                                    else if (attr.Equals("exif:XPSubject"))
                                        value = exif.GetValue(ExifTag.XPSubject) != null ? UNICODE.GetString(exif.GetValue(ExifTag.XPSubject).Value) : image.GetAttribute(attr) ?? string.Empty;
                                    if (!string.IsNullOrEmpty(value)) value = value.Replace("\0", string.Empty).TrimEnd('\0');
                                }
                                else if (attr.Equals("png:bKGD")) value = image.BackgroundColor.ToString();
                                else if (attr.Equals("png:cHRM"))
                                {
                                    var cr = XYZ2RGB(image.ChromaRedPrimary.X, image.ChromaRedPrimary.Y, image.ChromaRedPrimary.Z);
                                    var cg = XYZ2RGB(image.ChromaGreenPrimary.X, image.ChromaGreenPrimary.Y, image.ChromaGreenPrimary.Z);
                                    var cb = XYZ2RGB(image.ChromaBluePrimary.X, image.ChromaBluePrimary.Y, image.ChromaBluePrimary.Z);

                                    var r = $"[{image.ChromaRedPrimary.X:F5},{image.ChromaRedPrimary.Y:F5},{image.ChromaRedPrimary.Z:F5}]";
                                    var g = $"[{image.ChromaGreenPrimary.X:F5},{image.ChromaGreenPrimary.Y:F5},{image.ChromaGreenPrimary.Z:F5}]";
                                    var b = $"[{image.ChromaBluePrimary.X:F5},{image.ChromaBluePrimary.Y:F5},{image.ChromaBluePrimary.Z:F5}]";
                                    //value = $"R: {r}{Environment.NewLine}{" ".PadRight(36)}G: {g}{Environment.NewLine}{" ".PadRight(36)}B: {b}";
                                    value = $"R:{cr.ToString()}, G:{cg.ToString()}, B:{cb.ToString()}{Environment.NewLine}XYZ-R: {r}{Environment.NewLine}XYZ-G: {g}{Environment.NewLine}XYZ-B: {b}";
                                }
                                var values = value.Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
                                foreach (var v in values)
                                {
                                    if (v.Length > 64) value = $"{v.Substring(0, 64)} ...";
                                    var text = v.Equals(values.First()) ? $"{attr.PadRight(32)}= { v }" : $"{" ".PadRight(34)}{ v }";
                                    Log(text);
                                }
                                //tip.Add(text);
                            }
                            catch (Exception ex) { MessageBox.Show($"{attr} : {ex.Message}"); }
                        }
                        Log($"{"Profiles".PadRight(32)}= {string.Join(", ", image.ProfileNames)}");
                        foreach (var profile_name in image.ProfileNames)
                        {
                            try
                            {
                                var profile = image.GetProfile(profile_name);
                                var prefix = $"Profile {profile.Name}".PadRight(32, ' ');
                                var bytes = profile.ToByteArray().Select(b => $"{b}");
                                Log($"{prefix}= {bytes.Count()} bytes");// [{profile.GetType()}]");
                                //if (profile_name.Equals("8bim"))
                                //{
                                //    var _8bim = image.Get8BimProfile();
                                //    Log(_8bim.Values.Count());
                                //    foreach (var v in _8bim.Values) Log($"  0x{v.ID:X2} : {v.ToString()}");
                                //}
                                //Log($"{prefix}= {bytes.Count()} bytes [{string.Join(", ", bytes)}]");
                            }
                            catch (Exception ex) { Log(ex.Message); }
                        }
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
                                    Log($"  {$"{attr.Name}".PadRight(30)}= {attr.Value}");
                                }
                                foreach (XmlNode child in node.ChildNodes)
                                {
                                    foreach (XmlAttribute attr in child.Attributes)
                                    {
                                        if (string.IsNullOrEmpty(attr.Value)) continue;
                                        Log($"    {$"{attr.Name}".PadRight(28)}= {attr.Value}");
                                    }
                                    if (child.Name.Equals("dc:title", StringComparison.CurrentCultureIgnoreCase))
                                        Log($"    {"dc:Title".PadRight(28)}= {child.InnerText}");
                                    else if (child.Name.Equals("MicrosoftPhoto:DateAcquired", StringComparison.CurrentCultureIgnoreCase))
                                        Log($"    {"MicrosoftPhoto:DateAcquired".PadRight(28)}= {child.InnerText}");
                                    else if (child.Name.Equals("MicrosoftPhoto:DateTaken", StringComparison.CurrentCultureIgnoreCase))
                                        Log($"    {"MicrosoftPhoto:DateTaken".PadRight(28)}= {child.InnerText}");
                                    else if (child.Name.Equals("exif:DateTimeDigitized", StringComparison.CurrentCultureIgnoreCase))
                                        Log($"    {"exif:DateTimeDigitized".PadRight(28)}= {child.InnerText}");
                                    else if (child.Name.Equals("exif:DateTimeOriginal", StringComparison.CurrentCultureIgnoreCase))
                                        Log($"    {"exif:DateTimeOriginal".PadRight(28)}= {child.InnerText}");
                                    else if (child.Name.Equals("tiff:DateTime", StringComparison.CurrentCultureIgnoreCase))
                                        Log($"    {"tiff:DateTime".PadRight(28)}= {child.InnerText}");
                                    else Log($"    {$"{child.Name}".PadRight(28)}= {child.InnerText}");
                                }
                            }
                            Log($"{"  XML Contents".PadRight(32)}= {FormatXML(xml).Replace("\"", "'")}");
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
                if (image.AttributeNames.Count() > 0)
                {
                    result.Attributes = new Dictionary<string, string>();
                    foreach (var attr in image.AttributeNames) { try { result.Attributes.Add(attr, image.GetAttribute(attr)); } catch { } }
                }
                if (image.ProfileNames.Count() > 0)
                {
                    result.Profiles = new Dictionary<string, IImageProfile>();
                    foreach (var profile in image.ProfileNames) { try { result.Profiles.Add(profile, image.GetProfile(profile)); } catch { } }
                }

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

        public static void Main(string[] args)
        {
            InitMagicK();

            try
            {
                if (args.Length == 1)
                {
                    var fi = args[0];
                    var path = Path.GetDirectoryName(fi);
                    //Log(path);
                    if (!Path.IsPathRooted(path)) path = Path.Combine(Directory.GetCurrentDirectory(), path);
                    //Log(Path.GetFileName(fi));
                    var files = Directory.GetFiles(path, Path.GetFileName(fi), SearchOption.TopDirectoryOnly);
                    foreach (var file in files)
                    {
                        try
                        {
                            var fn = Path.GetFullPath(Path.Combine(path, file));
                            Log($"{fn}");
                            Log($"-".PadRight(80, '-'));
                            ShowMeta(fn);
                            Log("=".PadRight(80, '='));
                        }
                        catch (Exception ex) { Log(ex.Message); }
                    }
                }
                else if (args.Length >= 2)
                {
                    var opt = args[0];
                    var fi = args[1];
                    var path = Path.GetDirectoryName(fi);
                    //Log(path);
                    if (!Path.IsPathRooted(path)) path = Path.Combine(Directory.GetCurrentDirectory(), path);
                    //Log(Path.GetFileName(fi));
                    var files = Directory.GetFiles(path, Path.GetFileName(fi), SearchOption.TopDirectoryOnly);
                    foreach (var file in files)
                    {
                        try
                        {
                            var fn = Path.GetFullPath(Path.Combine(path, file));
                            Log($"{fn}");
                            Log($"-".PadRight(80, '-'));
                            if (opt.Equals("-M", StringComparison.CurrentCultureIgnoreCase)) TouchMeta(fn);
                            else if (opt.Equals("-MF", StringComparison.CurrentCultureIgnoreCase)) TouchMeta(fn, force: true);
                            else if (opt.Equals("-T", StringComparison.CurrentCultureIgnoreCase)) TouchDate(fn, args.Length >= 3 ? args[2] : null);
                            else if (opt.Equals("-C", StringComparison.CurrentCultureIgnoreCase)) ClearMeta(fn);
                            Log("=".PadRight(80, '='));
                        }
                        catch (Exception ex) { Log(ex.Message); }
                    }
                }
            }
            catch (Exception ex) { Log($"{ex.Message}{Environment.NewLine}{ex.StackTrace}"); }
            finally
            {

            }
        }
    }
}
