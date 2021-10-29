using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Xml;

using ImageMagick;

namespace NetCharm
{
    public class MetaInfo
    {
        public bool TouchProfiles { get; set; } = true;
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
        public int? Rating { get; set; } = null;
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

        private static List<string> SupportedFormats { get; set; } = null;

        #region Log/MessageBox helper
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

        private static void ShowMessage(string text, string title)
        {
            Log($"[{title}]{text}");
        }
        #endregion

        #region Text/Color Converting Helper
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

        private static string BytesToString(byte[] bytes, bool ascii = false)
        {
            var result = string.Empty;
            if (bytes is byte[] && bytes.Length > 0)
            {
                if (ascii) result = Encoding.ASCII.GetString(bytes);
                else
                {
                    var bytes_text = bytes.Select(c => ascii ? $"{Convert.ToChar(c)}" : $"{c}");
                    result = string.Join(", ", bytes_text);
                    if (bytes.Length > 4)
                    {
                        var text = BytesToUnicode(result);
                        if (!result.StartsWith("0,") && !string.IsNullOrEmpty(text)) result = text;
                    }
                }
            }
            return (result);
        }

        private static string BytesToUnicode(byte[] bytes, bool ascii = false)
        {
            var result = string.Empty;
            if (bytes is byte[] && bytes.Length > 0)
            {
                result = ascii ? Encoding.ASCII.GetString(bytes) : Encoding.Unicode.GetString(bytes);
            }
            return (result);
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
          //"xmp:DateTimeDigitized",
          //"xmp:DateTimeOriginal",
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
          "exif:UserComment"
        };
        private static string[] tag_keywords = new string[] {
          "exif:WinXP-Keywords",
        };
        private static string[] tag_rating = new string[] {
          "MicrosoftPhoto:Rating",
        };
        #endregion

        #region Metadata Helper
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
            catch (Exception ex) { ShowMessage(ex.Message); }
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

        public static string GetAttribute(MagickImage image, string attr)
        {
            string result = null;
            try
            {
                if (image is MagickImage && image.FormatInfo.IsReadable)
                {
                    var exif = image.HasProfile("exif") ? image.GetExifProfile() : new ExifProfile();
                    var iptc = image.HasProfile("iptc") ? image.GetIptcProfile() : new IptcProfile();

                    result = attr.Contains("WinXP") ? BytesToUnicode(image.GetAttribute(attr)) : image.GetAttribute(attr);
                    if (attr.StartsWith("exif:") && !attr.Contains("WinXP"))
                    {
                        Type exiftag_type = typeof(ExifTag);
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
                                    if (tag_value.Tag == ExifTag.ExifVersion)
                                        result = BytesToString(tag_value.GetValue() as byte[], true);
                                    else
                                        result = BytesToString(tag_value.GetValue() as byte[], false);
                                }
                                else if (tag_value.DataType == ExifDataType.Byte && tag_value.IsArray)
                                    result = BytesToString(tag_value.GetValue() as byte[]);
                                else if (tag_value.DataType == ExifDataType.Unknown && tag_value.IsArray)
                                    result = BytesToString(tag_value.GetValue() as byte[], tag_value.Tag.ToString().Contains("Version"));
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
                        Type exiftag_type = typeof(ExifTag);
                        var tag_name =  attr.Contains("WinXP") ? $"XP{attr.Substring(11)}" : attr.Substring(5);
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
                                exif.SetValue(tag_property.GetValue(exif), Encoding.Unicode.GetBytes(value));
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
                    }

                    //if (!image.HasProfile("exif")) 
                    image.SetProfile(exif);
                    //if (!image.HasProfile("iptc")) 
                    image.SetProfile(iptc);
                }
            }
            catch (Exception ex) { Log(ex.Message); }
        }

        public static void TouchProfile(string file, Dictionary<string, IImageProfile> profiles, bool force = false)
        {
            if (force && File.Exists(file) && profiles is Dictionary<string, IImageProfile>)
            {
                try
                {
                    var fi = new FileInfo(file);
                    using (MagickImage image = new MagickImage(fi.FullName))
                    {
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
                    using (MagickImage image = new MagickImage(fi.FullName))
                    {
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
            return (result);
        }

        public static DateTime? GetMetaTime(string file)
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
                    catch (Exception ex) { ShowMessage(ex.Message, "ERROR"); }
                }
                result = dm;
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
                        result.Author = GetAttribute(image, tag);
                        if (!string.IsNullOrEmpty(result.Author)) break;
                    }
                }
                #endregion
                #region Copyright
                foreach (var tag in tag_copyright)
                {
                    if (image.AttributeNames.Contains(tag))
                    {
                        result.Copyright = GetAttribute(image, tag);
                        if (!string.IsNullOrEmpty(result.Copyright)) break;
                    }
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
                        using (MagickImage image = new MagickImage(ms))
                        {
                            result = GetMetaInfo(image);
                        }
                    }
                    catch (Exception ex) { ShowMessage(ex.Message, "ERROR"); }
                }
            }
            else Log($"File \"{file}\" not exists!");
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
                    if (image.FormatInfo.IsReadable && image.FormatInfo.IsWritable)
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

                        FixDPI(image);
                        image.Write(fi.FullName);
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
            if (File.Exists(file))
            {
                try
                {
                    var fi = new FileInfo(file);

                    var title = meta is MetaInfo ? meta.Title ?? Path.GetFileNameWithoutExtension(fi.Name) : Path.GetFileNameWithoutExtension(fi.Name);
                    var subject = meta is MetaInfo ? meta.Subject : title;
                    var authors = meta is MetaInfo ? meta.Author : string.Empty;
                    var copyright = meta is MetaInfo ? meta.Copyright : authors;
                    var keywords = meta is MetaInfo ? meta.Keywords : string.Empty;
                    var comment = meta is MetaInfo ? meta.Comment : string.Empty;
                    var rating = meta is MetaInfo ? meta.Rating : null;
                    if (!string.IsNullOrEmpty(title)) title.Replace("\0", string.Empty).TrimEnd('\0');
                    if (!string.IsNullOrEmpty(subject)) subject.Replace("\0", string.Empty).TrimEnd('\0');
                    if (!string.IsNullOrEmpty(authors)) authors.Replace("\0", string.Empty).TrimEnd('\0');
                    if (!string.IsNullOrEmpty(copyright)) copyright.Replace("\0", string.Empty).TrimEnd('\0');
                    if (!string.IsNullOrEmpty(keywords)) keywords.Replace("\0", string.Empty).TrimEnd('\0');
                    if (!string.IsNullOrEmpty(comment)) comment.Replace("\0", string.Empty).TrimEnd('\0');

                    var keyword_list = string.IsNullOrEmpty(keywords) ? new List<string>() : keywords.Split(new char[] { ';', '#' }, StringSplitOptions.RemoveEmptyEntries).Select(k => k.Trim()).Where(k => !string.IsNullOrEmpty(k)).Distinct();
                    keywords = string.Join("; ", keyword_list);

                    using (MagickImage image = new MagickImage(fi.FullName))
                    {
                        if (image.FormatInfo.IsReadable && image.FormatInfo.IsWritable)
                        {
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
                            var dc = dtc ?? (meta is MetaInfo ? meta.DateCreated ?? meta.DateAcquired ?? meta.DateTaken : null) ?? fi.CreationTime;
                            var dm = dtm ?? (meta is MetaInfo ? meta.DateModified ?? meta.DateAcquired ?? meta.DateTaken : null) ?? fi.LastWriteTime;
                            var da = dta ?? (meta is MetaInfo ? meta.DateAccesed ?? meta.DateAcquired ?? meta.DateTaken : null) ?? fi.LastAccessTime;

                            if (!force)
                            {
                                var dt = GetMetaTime(image);
                                dc = dt ?? (meta is MetaInfo ? meta.DateAcquired ?? meta.DateTaken : null) ?? fi.CreationTime;
                                dm = dt ?? (meta is MetaInfo ? meta.DateAcquired ?? meta.DateTaken : null) ?? fi.LastWriteTime;
                                da = dt ?? (meta is MetaInfo ? meta.DateAcquired ?? meta.DateTaken : null) ?? fi.LastAccessTime;
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
                                        else if (tag.StartsWith("xmp")) SetAttribute(image, tag, dm_date);
                                        else SetAttribute(image, tag, dm_misc);

                                        var value_new = GetAttribute(image, tag);
                                        Log($"{$"{tag}".PadRight(32)}= {(value_old == null ? "NULL" : value_old)} => {value_new}");
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
                                            if (exif.GetValue(ExifTag.XPTitle) == null)
                                            {
                                                if (!string.IsNullOrEmpty(title)) SetAttribute(image, tag, value_old);
                                            }
                                            else title = GetAttribute(image, tag);
                                        }
                                        else if (tag.Equals("exif:ImageDescription"))
                                        {
                                            if (exif.GetValue(ExifTag.ImageDescription) == null)
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
                            #endregion
                            #region touch subject
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
                                            if (exif.GetValue(ExifTag.XPSubject) == null)
                                            {
                                                if (!string.IsNullOrEmpty(subject)) exif.SetValue(ExifTag.XPSubject, Encoding.Unicode.GetBytes(value_old));
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
                                            if (exif.GetValue(ExifTag.XPAuthor) == null)
                                            {
                                                if (!string.IsNullOrEmpty(authors)) SetAttribute(image, tag, value_old);
                                            }
                                            else authors = GetAttribute(image, tag);
                                        }
                                        else if (tag.Equals("exif:Artist"))
                                        {
                                            if (exif.GetValue(ExifTag.Artist) == null)
                                            {
                                                if (!string.IsNullOrEmpty(authors)) SetAttribute(image, tag, value_old);
                                            }
                                            else authors = GetAttribute(image, tag);
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
                                    var value_old = GetAttribute(image, tag);
                                    if (force || (!image.AttributeNames.Contains(tag) && !string.IsNullOrEmpty(copyright)))
                                    {
                                        SetAttribute(image, tag, copyright);
                                        var value_new = GetAttribute(image, tag);
                                        Log($"{$"{tag}".PadRight(32)}= {(value_old == null ? "NULL" : value_old)} => {value_new}");
                                    }
                                    else
                                    {
                                        if (tag.Equals("exif:Copyright"))
                                        {
                                            if (exif.GetValue(ExifTag.Copyright) == null)
                                            {
                                                if (!string.IsNullOrEmpty(copyright)) SetAttribute(image, tag, value_old);
                                            }
                                            else copyright = GetAttribute(image, tag);
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
                                            if (exif.GetValue(ExifTag.XPComment) == null)
                                            {
                                                if (!string.IsNullOrEmpty(comment)) SetAttribute(image, tag, value_old);
                                            }
                                            else comment = GetAttribute(image, tag);
                                        }
                                        else if (tag.Equals("exif:WinXP-Comments"))
                                        {
                                            if (exif.GetValue(ExifTag.XPComment) == null)
                                            {
                                                if (!string.IsNullOrEmpty(comment)) SetAttribute(image, tag, value_old);
                                            }
                                            else comment = GetAttribute(image, tag);
                                        }
                                        else if (tag.Equals("exif:UserComment"))
                                        {
                                            if (exif.GetValue(ExifTag.UserComment) == null)
                                            {
                                                if (!string.IsNullOrEmpty(comment)) SetAttribute(image, tag, value_old);
                                            }
                                            else comment = GetAttribute(image, tag);
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
                                            if (exif.GetValue(ExifTag.XPKeywords) == null)
                                            {
                                                if (!string.IsNullOrEmpty(keywords)) SetAttribute(image, tag, value_old);
                                            }
                                            else keywords = GetAttribute(image, tag);
                                        }
                                    }
                                }
                                catch (Exception ex) { Log(ex.Message); }
                            }
                            #endregion
                            #region touch rating
                            foreach (var tag in tag_rating)
                            {
                                try
                                {
                                    var value_old = GetAttribute(image, tag);
                                    if (force || (!image.AttributeNames.Contains(tag) && rating.HasValue))
                                    {
                                        SetAttribute(image, tag, rating);
                                        var value_new = GetAttribute(image, tag);
                                        Log($"{$"{tag}".PadRight(32)}= {(value_old == null ? "NULL" : value_old)} => {value_new}");
                                    }
                                    else
                                    {
                                        if (tag.Equals("MicrosoftPhoto:Rating"))
                                        {
                                            if (exif.GetValue(ExifTag.RatingPercent) == null)
                                            {
                                                if (!string.IsNullOrEmpty(keywords)) SetAttribute(image, tag, value_old);
                                            }
                                            else rating = Convert.ToInt32(GetAttribute(image, tag));
                                        }
                                    }
                                }
                                catch (Exception ex) { Log(ex.Message); }
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

                                xmp = new XmpProfile(Encoding.UTF8.GetBytes(xml));
                                image.SetProfile(xmp);
                            }
                            #endregion
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
                                            desc.SetAttribute("xmlns:dc", xmp_ns_lookup["dc"]);
                                            desc.AppendChild(xml_doc.CreateElement("dc:title", "dc"));
                                            root_node.AppendChild(desc);
                                        }
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

                                        #region xml nodes updating
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
                                            foreach (XmlNode child in node.ChildNodes)
                                            {
                                                if (child.Name.Equals("dc:title", StringComparison.CurrentCultureIgnoreCase) ||
                                                    child.Name.Equals("dc:description", StringComparison.CurrentCultureIgnoreCase))
                                                {
                                                    child.RemoveAll();
                                                    var node_title = xml_doc.CreateElement("rdf:Alt", "rdf");
                                                    var node_title_li = xml_doc.CreateElement("rdf:li", "rdf");
                                                    node_title_li.SetAttribute("xml:lang", "x-default");
                                                    node_title_li.InnerText = title;
                                                    node_title.AppendChild(node_title_li);
                                                    child.AppendChild(node_title);
                                                }
                                                else if (child.Name.Equals("xmp:creator", StringComparison.CurrentCultureIgnoreCase))
                                                {
                                                    child.RemoveAll();
                                                    var node_author = xml_doc.CreateElement("rdf:Seq", "rdf");
                                                    node_author.SetAttribute("xmlns:rdf", "http://www.w3.org/1999/02/22-rdf-syntax-ns#");
                                                    add_rdf_li.Invoke(node_author, authors);
                                                    child.AppendChild(node_author);
                                                }
                                                else if (child.Name.Equals("dc:subject", StringComparison.CurrentCultureIgnoreCase) ||
                                                    child.Name.StartsWith("MicrosoftPhoto:LastKeyword", StringComparison.CurrentCultureIgnoreCase))
                                                {
                                                    child.RemoveAll();
                                                    var node_subject = xml_doc.CreateElement("rdf:Bag", "rdf");
                                                    node_subject.SetAttribute("xmlns:rdf", "http://www.w3.org/1999/02/22-rdf-syntax-ns#");
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
                                                    child.InnerText = dm_xmp;
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
                                xmp = new XmpProfile(Encoding.UTF8.GetBytes(xml));
                                image.SetProfile(xmp);
                                Log($"{"XMP Profiles".PadRight(32)}= {xml}");
                            }
                            #endregion

                            FixDPI(image);
                            image.Write(fi.FullName);

                            fi.CreationTime = dc;
                            fi.LastWriteTime = dm;
                            fi.LastAccessTime = da;
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

        public static void ShowMeta(string file, bool xmp_merge_nodes = false)
        {
            if (File.Exists(file))
            {
                var fi = new FileInfo(file);
                using (var ms = new FileStream(fi.FullName, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    try
                    {
                        using (MagickImage image = new MagickImage(ms))
                        {
                            if (image.FormatInfo.IsReadable)
                            {
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
                                Log($"{"Compression".PadRight(cw)}= {image.Compression.ToString()}");
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
                                            else if (child.Name.Equals("dc:creator") || child.Name.Equals("dc:subject") ||
                                                child.Name.StartsWith("MicrosoftPhoto:LastKeyword"))
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
                                    Log($"{"  XML Contents".PadRight(cw)}= {FormatXML(xml, xmp_merge_nodes)}");
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

                SupportedFormats = ((MagickFormat[])Enum.GetValues(typeof(MagickFormat))).Select(e => $".{e.ToString().ToLower()}").ToList();
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
