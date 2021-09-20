using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;

using ImageMagick;

namespace NetChamr
{
    class Metadata
    {
        private static string AppExec = Application.ResourceAssembly.CodeBase.ToString().Replace("file:///", "").Replace("/", "\\");
        private static string AppPath = Path.GetDirectoryName(AppExec);
        private static string AppName = Path.GetFileNameWithoutExtension(AppPath);
        private static string CachePath =  "cache";

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
        private static string[] tag_artist = new string[] {
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
        private static string[] tag_kywords = new string[] {
          "exif:WinXP-Keywords",
        };
        private static string[] tag_rating = new string[] {
          "MicrosoftPhoto:Rating",
        };
        #endregion

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
                            Console.WriteLine($"Try remove attribute '{attr}' ...");
                            image.RemoveAttribute(attr);
                        }
                        catch (Exception ex) { Console.WriteLine(ex.Message); }
                    }
                    foreach (var profile_name in image.ProfileNames)
                    {
                        try
                        {
                            Console.WriteLine($"Try remove profile '{profile_name}' ...");
                            image.RemoveProfile(image.GetProfile(profile_name));
                        }
                        catch (Exception ex) { Console.WriteLine(ex.Message); }
                    }

                    image.Write(fi.FullName);
                }
                fi.CreationTime = dc;
                fi.LastWriteTime = dm;
                fi.LastAccessTime = da;
            }
        }

        public static void TouchMeta(string file, bool force = false)
        {
            if (File.Exists(file))
            {
                var fi = new FileInfo(file);
                var dc = fi.CreationTime;
                var dm = fi.LastWriteTime;
                var da = fi.LastAccessTime;

                // 2021:09:13 11:00:16
                var dc_exif = dc.ToString("yyyy:MM:dd HH:mm:ss");
                var dm_exif = dc.ToString("yyyy:MM:dd HH:mm:ss");
                var da_exif = dc.ToString("yyyy:MM:dd HH:mm:ss");
                // 2021-09-13T06:38:49+00:00
                var dc_date = dc.ToString("yyyy-MM-ddTHH:mm:sszzz");
                var dm_date = dc.ToString("yyyy-MM-ddTHH:mm:sszzz");
                var da_date = dc.ToString("yyyy-MM-ddTHH:mm:sszzz");
                // 2021-08-26T12:23:49
                var dc_ms = dc.ToString("yyyy-MM-ddTHH:mm:sszzz");
                var dm_ms = dc.ToString("yyyy-MM-ddTHH:mm:sszzz");
                var da_ms = dc.ToString("yyyy-MM-ddTHH:mm:sszzz");
                // 2021-08-26T12:23:49.002
                var dc_msxmp = dc.ToString("yyyy-MM-ddTHH:mm:ss.fff");
                var dm_msxmp = dc.ToString("yyyy-MM-ddTHH:mm:ss.fff");
                var da_msxmp = dc.ToString("yyyy-MM-ddTHH:mm:ss.fff");
                // 2021-09-13T08:38:13Z
                var dc_png = dc.ToString("yyyy-MM-ddTHH:mm:ssZ");
                var dm_png = dc.ToString("yyyy-MM-ddTHH:mm:ssZ");
                var da_png = dc.ToString("yyyy-MM-ddTHH:mm:ssZ");
                // 2021:09:13 11:00:16+08:00
                var dc_misc = dc.ToString("yyyy:MM:dd HH:mm:sszzz");
                var dm_misc = dc.ToString("yyyy:MM:dd HH:mm:sszzz");
                var da_misc = dc.ToString("yyyy:MM:dd HH:mm:sszzz");

                //Console.WriteLine($"date : {dm_date}");
                //Console.WriteLine($"exif : {dm_exif}");
                //Console.WriteLine($"png  : {dm_png}");
                //Console.WriteLine($"MS   : {dm_ms}");
                //Console.WriteLine($"misc : {dm_misc}");

                var title = Path.GetFileNameWithoutExtension(fi.Name);

                using (MagickImage image = new MagickImage(fi.FullName))
                {
                    bool is_png = image.FormatInfo.MimeType.Equals("image/png", StringComparison.CurrentCultureIgnoreCase);
                    bool is_jpg = image.FormatInfo.MimeType.Equals("image/jpeg", StringComparison.CurrentCultureIgnoreCase);

                    var exif = image.HasProfile("exif") ? image.GetExifProfile() : new ExifProfile();
                    var xmp = image.HasProfile("xmp") ? image.GetXmpProfile() : null;

                    foreach (var tag in tag_date)
                    {
                        try
                        {
                            //Console.WriteLine(tag);
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
                                Console.WriteLine($"{$"  {tag}".PadRight(32)}= {(value_old == null ? "NULL" : value_old)} => {image.GetAttribute(tag)}");
                            }
                        }
                        catch (Exception ex) { Console.WriteLine(ex.Message); }
                    }
                    foreach (var tag in tag_title)
                    {
                        try
                        {
                            if (force || !image.AttributeNames.Contains(tag))
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
                                }
                                Console.WriteLine($"{$"  {tag}".PadRight(32)}= {(value_old == null ? "NULL" : value_old)} => {(tag.Contains("WinXP") ? BytesToUnicode(image.GetAttribute(tag)) : image.GetAttribute(tag))}");
                            }
                        }
                        catch (Exception ex) { Console.WriteLine(ex.Message); }
                    }
                    foreach (var tag in tag_subject)
                    {
                        try
                        {
                            if (force || !image.AttributeNames.Contains(tag))
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
                                    if (tag.StartsWith("exif") && tag.Substring(5).Equals("WinXP-Subject"))
                                        exif.SetValue(ExifTag.XPSubject, Encoding.Unicode.GetBytes(title));
                                }
                                Console.WriteLine($"{$"  {tag}".PadRight(32)}= {(value_old == null ? "NULL" : value_old)} => {(tag.Contains("WinXP") ? BytesToUnicode(image.GetAttribute(tag)) : image.GetAttribute(tag))}");
                            }
                        }
                        catch (Exception ex) { Console.WriteLine(ex.Message); }
                    }
                    foreach (var tag in tag_comments)
                    {
                        try
                        {
                            if (force || !image.AttributeNames.Contains(tag))
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
                                    if (tag.StartsWith("exif") && tag.Substring(5).Equals("WinXP-Comment"))
                                        exif.SetValue(ExifTag.XPComment, Encoding.Unicode.GetBytes(title));
                                    else if (tag.StartsWith("exif") && tag.Substring(5).Equals("WinXP-Comments"))
                                        exif.SetValue(ExifTag.XPComment, Encoding.Unicode.GetBytes(title));
                                    else if (tag.StartsWith("exif") && tag.Substring(5).Equals("ImageDescription"))
                                        exif.SetValue(ExifTag.ImageDescription, title);
                                }
                                Console.WriteLine($"{$"  {tag}".PadRight(32)}= {(value_old == null ? "NULL" : value_old)} => {(tag.Contains("WinXP") ? BytesToUnicode(image.GetAttribute(tag)) : image.GetAttribute(tag))}");
                            }
                        }
                        catch (Exception ex) { Console.WriteLine(ex.Message); }
                    }
                    //foreach (var tag in tag_keywords)
                    //{
                    //    if (force || !image.AttributeNames.Contains(tag))
                    //    {
                    //        var value_old = tag.Contains("WinXP") ? BytesToUnicode(image.GetAttribute(tag)) : image.GetAttribute(tag);
                    //        if (tag.StartsWith("exif"))
                    //        {
                    //            if (tag.Contains("WinXP")) image.SetAttribute(tag, UnicodeToBytes(title));
                    //            else image.SetAttribute(tag, title);
                    //        }
                    //        else if (tag.StartsWith("png")) image.SetAttribute(tag, title);
                    //        else if (tag.StartsWith("Microsoft")) image.SetAttribute(tag, title);
                    //        Console.WriteLine($"{$"  {tag}".PadRight(32)}= {value_old} => {(tag.Contains("WinXP") ? BytesToUnicode(image.GetAttribute(tag)) : image.GetAttribute(tag))}");
                    //    }
                    //}

                    Console.WriteLine($"{"  Profiles".PadRight(32)}= {string.Join(", ", image.ProfileNames)}");

                    if (exif != null) image.SetProfile(exif);
                    if (xmp != null)
                    {
                        var pattern_ms_da = @"(<MicrosoftPhoto:DateAcquired>).*?(</MicrosoftPhoto:DateAcquired>)";
                        var xml = Encoding.UTF8.GetString(xmp.GetData());
                        if (Regex.IsMatch(xml, pattern_ms_da, RegexOptions.IgnoreCase))
                            xml = Regex.Replace(xml, pattern_ms_da, $"$1{dm_msxmp}$2", RegexOptions.IgnoreCase);
                        else
                        {
                            var msda_xml = $"<rdf:Description rdf:about=\"uuid:faf5bdd5-ba3d-11da-ad31-d33d75182f1b\" xmlns:MicrosoftPhoto=\"http://ns.microsoft.com/photo/1.0/\"><MicrosoftPhoto:DateAcquired>{dm_msxmp}</MicrosoftPhoto:DateAcquired></rdf:Description>";
                            xml = Regex.Replace(xml, @"(</rdf:RDF></x:xmpmeta>)", $"{msda_xml}$1", RegexOptions.IgnoreCase);
                        }
                        //Console.WriteLine(xml);
                        xmp = new XmpProfile(Encoding.UTF8.GetBytes(xml));
                        image.SetProfile(xmp);
                    }
                    else
                    {
                        var xml = $"<?xpacket begin='?' id='W5M0MpCehiHzreSzNTczkc9d'?>{Environment.NewLine}<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\"><rdf:Description rdf:about=\"uuid:faf5bdd5-ba3d-11da-ad31-d33d75182f1b\" xmlns:MicrosoftPhoto=\"http://ns.microsoft.com/photo/1.0/\"><MicrosoftPhoto:DateAcquired>{dm_msxmp}</MicrosoftPhoto:DateAcquired></rdf:Description></rdf:RDF></x:xmpmeta>{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}                            <?xpacket end='w'?>";
                        //Console.WriteLine(xml);
                        xmp = new XmpProfile(Encoding.UTF8.GetBytes(xml));
                        image.SetProfile(xmp);
                    }
                    image.Write(fi.FullName);

                    fi.CreationTime = dc;
                    fi.LastWriteTime = dm;
                    fi.LastAccessTime = da;
                }
            }
        }

        public static void TouchDate(string file, string dt = null)
        {
            if (File.Exists(file))
            {
                var fi = new FileInfo(file);
                var dc = fi.CreationTime;
                var dm = fi.LastWriteTime;
                var da = fi.LastAccessTime;

                if (string.IsNullOrEmpty(dt))
                {
                    using (var ms = new FileStream(fi.FullName, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
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
                                    Console.WriteLine($"{tag.PadRight(32)}= {v} > {nv}");
                                    dm = DateTime.Parse(tag.Contains("png") ? nv.Substring(0, tag.Length - 1) : nv);
                                    break;
                                }
                            }
                        }
                    }
                    Console.WriteLine($"Touching Date To {dm.ToString("yyyy-MM-ddTHH:mm:sszzz")}");
                    fi.CreationTime = dm;
                    fi.LastWriteTime = dm;
                    fi.LastAccessTime = dm;
                }
                else
                {
                    try
                    {
                        var t = DateTime.Now;
                        if (DateTime.TryParse(dt, out t))
                        {
                            Console.WriteLine($"Touching Date To {t.ToString("yyyy-MM-ddTHH:mm:sszzz")}");
                            fi.CreationTime = t;
                            fi.LastWriteTime = t;
                            fi.LastAccessTime = t;
                        }
                    }
                    catch (Exception ex) { Console.WriteLine($"{ex.Message}{Environment.NewLine}{ex.StackTrace}"); }
                }
            }
        }

        public static void ShowMeta(string file)
        {
            if (File.Exists(file))
            {
                var fi = new FileInfo(file);
                using (var ms = new FileStream(fi.FullName, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    using (MagickImage image = new MagickImage(ms))
                    {
                        foreach (var attr in image.AttributeNames)
                        {
                            try
                            {
                                var value = image.GetAttribute(attr);
                                if (string.IsNullOrEmpty(value)) continue;
                                if (attr.Contains("WinXP")) value = BytesToUnicode(value);
                                if (value.Length > 64) value = $"{value.Substring(0, 64)} ...";
                                var text = $"  {attr.PadRight(32, ' ')}= { value }";
                                Console.WriteLine(text);
                                //tip.Add(text);
                            }
                            catch (Exception ex) { MessageBox.Show($"{attr} : {ex.Message}"); }
                        }
                        Console.WriteLine($"  {"Profiles".PadRight(32)}= {string.Join(", ", image.ProfileNames)}");
                        foreach (var profile_name in image.ProfileNames)
                        {
                            try
                            {
                                var profile = image.GetProfile(profile_name);
                                var prefix = $"Profile {profile.Name}".PadRight(32, ' ');
                                var bytes = profile.ToByteArray().Select(b => $"{b}");
                                Console.WriteLine($"  {prefix}= {bytes.Count()} bytes");// [{profile.GetType()}]");
                                //if (profile_name.Equals("8bim"))
                                //{
                                //    var _8bim = image.Get8BimProfile();
                                //    Console.WriteLine(_8bim.Values.Count());
                                //    foreach (var v in _8bim.Values) Console.WriteLine($"    0x{v.ID:X2} : {v.ToString()}");
                                //}
                                //Console.WriteLine($"  {prefix}= {bytes.Count()} bytes [{string.Join(", ", bytes)}]");
                            }
                            catch (Exception ex) { Console.WriteLine(ex.Message); }
                        }
                        Console.WriteLine($"  {"Color Space".PadRight(32)}= {Path.GetFileName(image.ColorSpace.ToString())}");
                        Console.WriteLine($"  {"Format Info".PadRight(32)}= {image.FormatInfo.Format.ToString()}, {image.FormatInfo.MimeType}");
                        var xmp = image.HasProfile("xmp") ? image.GetXmpProfile() : null;
                        if (xmp != null)
                        {
                            var xml = Encoding.UTF8.GetString(xmp.GetData());
                            Console.WriteLine(xml);
                        }
                    }
                }
            }
        }

        public static void Main(string[] args)
        {
            try
            {
                var magick_cache = Path.IsPathRooted(CachePath) ? CachePath : Path.Combine(AppPath, CachePath);
                if (!Directory.Exists(magick_cache)) Directory.CreateDirectory(magick_cache);
                if (Directory.Exists(magick_cache)) MagickAnyCPU.CacheDirectory = magick_cache;
                //ImageMagick.ResourceLimits.Area = 4096 * 4096;
                //ImageMagick.ResourceLimits.
                ImageMagick.ResourceLimits.Memory = 256 * 1024 * 1024;
                //ImageMagick.ResourceLimits.Throttle = 
                ImageMagick.ResourceLimits.Thread = 2;
                ImageMagick.ResourceLimits.LimitMemory(new Percentage(5));
            }
            catch (Exception ex) { Console.WriteLine(ex.Message); }

            try
            {
                if (args.Length == 1)
                {
                    var fi = args[0];
                    var path = Path.GetDirectoryName(fi);
                    //Console.WriteLine(path);
                    if (!Path.IsPathRooted(path)) path = Path.Combine(Directory.GetCurrentDirectory(), path);
                    //Console.WriteLine(Path.GetFileName(fi));
                    var files = Directory.GetFiles(path, Path.GetFileName(fi), SearchOption.TopDirectoryOnly);
                    foreach (var file in files)
                    {
                        try
                        {
                            var fn = Path.GetFullPath(Path.Combine(path, file));
                            Console.WriteLine($"  {fn}");
                            Console.WriteLine($"-".PadRight(80, '-'));
                            ShowMeta(fn);
                            Console.WriteLine("=".PadRight(80, '='));
                        }
                        catch (Exception ex) { Console.WriteLine(ex.Message); }
                    }
                }
                else if (args.Length >= 2)
                {
                    var opt = args[0];
                    var fi = args[1];
                    var path = Path.GetDirectoryName(fi);
                    //Console.WriteLine(path);
                    if (!Path.IsPathRooted(path)) path = Path.Combine(Directory.GetCurrentDirectory(), path);
                    //Console.WriteLine(Path.GetFileName(fi));
                    var files = Directory.GetFiles(path, Path.GetFileName(fi), SearchOption.TopDirectoryOnly);
                    foreach (var file in files)
                    {
                        try
                        {
                            var fn = Path.GetFullPath(Path.Combine(path, file));
                            Console.WriteLine($"  {fn}");
                            Console.WriteLine($"-".PadRight(80, '-'));
                            if (opt.Equals("-M", StringComparison.CurrentCultureIgnoreCase)) TouchMeta(fn);
                            else if (opt.Equals("-MF", StringComparison.CurrentCultureIgnoreCase)) TouchMeta(fn, force: true);
                            else if (opt.Equals("-T", StringComparison.CurrentCultureIgnoreCase)) TouchDate(fn, args.Length >= 3 ? args[2] : null);
                            else if (opt.Equals("-C", StringComparison.CurrentCultureIgnoreCase)) ClearMeta(fn);
                            Console.WriteLine("=".PadRight(80, '='));
                        }
                        catch (Exception ex) { Console.WriteLine(ex.Message); }
                    }
                }
            }
            catch (Exception ex) { Console.WriteLine($"{ex.Message}{Environment.NewLine}{ex.StackTrace}"); }
            finally
            {

            }
        }
    }
}
