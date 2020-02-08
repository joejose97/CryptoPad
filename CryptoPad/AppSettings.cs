﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using System.Xml.Serialization;

namespace CryptoPad
{
    [Serializable]
    public class AppSettings
    {
        [XmlIgnore]
        public string KeyStorage { get; private set; }

        [XmlIgnore]
        public SettingsType Type { get; private set; }

        public Size WindowSize { get; set; }

        public FormWindowState WindowStartupState { get; set; }

        public ColorCode EditorForegroundColor { get; set; }

        public ColorCode EditorBackgroundColor { get; set; }

        public string FontName { get; set; }
        public float FontSize { get; set; }
        public FontStyle FontStyle { get; set; }

        public Restrictions Restrictions { get; set; }

        public static string PortableSettingsFile
        {
            get
            {
                var Module = Assembly.GetExecutingAssembly().Modules.First().FullyQualifiedName;
                return Path.Combine(Path.GetDirectoryName(Module), "settings.xml");
            }
        }

        public static string GlobalSettingsFile
        {
            get
            {
                return Environment.ExpandEnvironmentVariables(@"%ProgramData%\CryptoPad\settings.xml");
            }
        }

        public static string UserSettingsFile
        {
            get
            {
                return Environment.ExpandEnvironmentVariables(@"%APPDATA%\CryptoPad\settings.xml");
            }
        }

        public AppSettings()
        {
            WindowStartupState = FormWindowState.Normal;
            WindowSize = new Size(600, 600);

            EditorForegroundColor = new ColorCode(Color.FromKnownColor(KnownColor.WindowText).Name);
            EditorBackgroundColor = new ColorCode(Color.FromKnownColor(KnownColor.Window).Name);

            SetFont(SystemFonts.DefaultFont);

            Type = SettingsType.Local;
        }

        public RSAKey[] LoadRSAKeys()
        {
            var Params = new List<RSAKey>();
            if (Directory.Exists(KeyStorage))
            {
                foreach (var F in Directory.GetFiles(KeyStorage, "*.xml"))
                {
                    try
                    {
                        var K = Tools.FromXML<RSAKey>(File.ReadAllText(F));
                        if (K.IsValid())
                        {
                            Params.Add(K);
                        }
                    }
                    catch
                    {
                        //Invalid key maybe?
                        try
                        {
                            File.Move(F, Path.ChangeExtension(F, ".invalid"));
                        }
                        catch
                        {
                            //Can't rename it either. Just skip
                        }
                    }
                }
            }
            return Params.ToArray();
        }

        public void SaveRSAKeys(IEnumerable<RSAKey> Keys, bool Purge = false)
        {
            if (!Directory.Exists(KeyStorage))
            {
                Directory.CreateDirectory(KeyStorage);
            }
            if (Purge)
            {
                foreach (var F in Directory.GetFiles(KeyStorage, "*.xml"))
                {
                    try
                    {
                        File.Delete(F);
                    }
                    catch
                    {
                        //Don't care
                        //We can't abort because it would leave a partially deleted directory
                    }
                }
                SaveRSAKeys(Keys, false);
            }
            else
            {
                var ExistingKeys = new List<RSAKey>(LoadRSAKeys());
                foreach (var Key in Keys.Where(m => m.IsValid()))
                {
                    if (!ExistingKeys.Any(m => m.Equals(Key)))
                    {
                        ExistingKeys.Add(Key);
                    }
                }
                foreach (var K in ExistingKeys)
                {
                    var Data = K.ToXML().Trim();
                    File.WriteAllText(Path.Combine(KeyStorage, Encryption.HashSHA256(Data) + ".xml"), Data);
                }
            }
        }

        public Font GetFont()
        {
            return new Font(FontName, FontSize, FontStyle);
        }

        public Font SetFont(Font F)
        {
            FontName = F.Name;
            FontSize = F.Size;
            FontStyle = F.Style;
            return F;
        }

        public static AppSettings GetSettings()
        {
            AppSettings ASGlobal = null;
            AppSettings ASLocal = null;

            try
            {
                //Portable settings win over all others
                var Settings = Tools.FromXML<AppSettings>(File.ReadAllText(PortableSettingsFile));
                Settings.KeyStorage = Path.Combine(Path.GetDirectoryName(PortableSettingsFile), "Keys");
                Settings.Type = SettingsType.Portable;
                //Don't allow restrictions in portable mode
                Settings.Restrictions = null;
                return Settings;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Unable to deserialize portable file: {ex.Message}");
            }

            //Read global and local settings
            try
            {
                ASGlobal = Tools.FromXML<AppSettings>(File.ReadAllText(GlobalSettingsFile));
                ASGlobal.KeyStorage = Path.Combine(Path.GetDirectoryName(GlobalSettingsFile), "Keys");
                ASGlobal.Type = SettingsType.Global;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Unable to deserialize global settings file: {ex.Message}");
            }
            try
            {
                ASLocal = Tools.FromXML<AppSettings>(File.ReadAllText(UserSettingsFile));
                ASLocal.KeyStorage = Path.Combine(Path.GetDirectoryName(UserSettingsFile), "Keys");
                ASLocal.Type = SettingsType.Local;
                //Ignore restrictions in local file
                ASLocal.Restrictions = null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Unable to deserialize user settings file: {ex.Message}");
            }
            //Return local settings if present
            if (ASGlobal == null)
            {
                if (ASLocal == null)
                {
                    Debug.WriteLine($"No settings present. Probably first run");
                    //Invent new local settings
                    return new AppSettings()
                    {
                        KeyStorage = Path.Combine(Path.GetDirectoryName(UserSettingsFile), "Keys"),
                        Type = SettingsType.Local
                    };
                }
                return ASLocal;
            }
            if (ASGlobal.Restrictions == null)
            {
                ASGlobal.Restrictions = new Restrictions();
            }
            return ASGlobal;
        }

        public static AppSettings GlobalSettings()
        {
            try
            {
                var ASGlobal = Tools.FromXML<AppSettings>(File.ReadAllText(GlobalSettingsFile));
                ASGlobal.KeyStorage = Path.Combine(Path.GetDirectoryName(GlobalSettingsFile), "Keys");
                ASGlobal.Type = SettingsType.Global;
                return ASGlobal;
            }
            catch
            {
                return null;
            }
        }

        public AppSettings SaveSettings(SettingsType Mode = 0)
        {
            var Data = Tools.ToXML(this);
            //Auto detect mode
            if (Mode == 0)
            {
                Restrictions = null;
                if (File.Exists(PortableSettingsFile))
                {
                    File.WriteAllText(PortableSettingsFile, Data);
                    Type = SettingsType.Portable;
                    KeyStorage = Path.Combine(Path.GetDirectoryName(PortableSettingsFile), "Keys");
                }
                else
                {
                    //Create settings directory
                    var DirName = Path.GetDirectoryName(UserSettingsFile);
                    try
                    {
                        Directory.CreateDirectory(DirName);
                    }
                    catch
                    {
                        //Don't care
                    }
                    File.WriteAllText(UserSettingsFile, Data);
                    Type = SettingsType.Local;
                    KeyStorage = Path.Combine(Path.GetDirectoryName(UserSettingsFile), "Keys");
                }
            }
            else
            {
                switch (Mode)
                {
                    case SettingsType.Local:
                        Restrictions = null;
                        KeyStorage= Path.Combine(Path.GetDirectoryName(UserSettingsFile), "Keys");
                        File.WriteAllText(UserSettingsFile, Data);
                        break;
                    case SettingsType.Global:
                        KeyStorage = Path.Combine(Path.GetDirectoryName(GlobalSettingsFile), "Keys");
                        File.WriteAllText(GlobalSettingsFile, Data);
                        break;
                    case SettingsType.Portable:
                        Restrictions = null;
                        KeyStorage = Path.Combine(Path.GetDirectoryName(PortableSettingsFile), "Keys");
                        File.WriteAllText(PortableSettingsFile, Data);
                        break;
                    default:
                        throw new NotImplementedException($"The given {nameof(SettingsType)} value is invalid");
                }
                Type = Mode;
            }
            return this;
        }
    }

    public enum SettingsType : int
    {
        Global = 1,
        Local = 2,
        Portable = 3
    }

    [Serializable]
    public class Restrictions
    {
        /// <summary>
        /// Minimum (inclusive) RSA key size in bits
        /// </summary>
        public int MinimumRsaSize { get; set; }

        /// <summary>
        /// Disallowed modes for encrypting files
        /// </summary>
        public CryptoMode[] BlockedModes { get; set; }

        /// <summary>
        /// Disallow conversion into portable version
        /// </summary>
        public bool BlockPortable { get; set; }
        /// <summary>
        /// If set will add these keys to newly encrypted files.
        /// </summary>
        public RSAKey[] AutoRsaKeys { get; set; }
    }

    [Serializable]
    public class ColorCode
    {
        private string _name;
        private int _value;

        [XmlAttribute]
        public string Name
        {
            get { return _name; }
            set
            {
                if (!string.IsNullOrEmpty(value))
                {
                    _value = Color.FromName(value).ToArgb();
                }
                _name = value;
            }
        }

        [XmlAttribute]
        public int Value
        {
            get { return _value; }
            set
            {
                var v = value | (0xFF << 24);
                if (v != _value)
                {
                    _name = FindColorName(v);
                    _value = v;
                }
            }
        }

        public ColorCode() : this(Color.Transparent)
        {
            //NOOP
        }

        public ColorCode(string Name)
        {
            this.Name = Name;
            Value = Color.FromName(Name).ToArgb();
        }

        public ColorCode(int Argb)
        {
            Name = null;
            Value = Argb;
        }

        public ColorCode(Color ExistingColor)
        {
            Name = ExistingColor.IsNamedColor ? ExistingColor.Name : null;
            Value = ExistingColor.ToArgb();
        }

        public Color GetColor()
        {
            return string.IsNullOrEmpty(Name) ? Color.FromArgb(Value) : Color.FromName(Name);
        }

        public static string FindColorName(int Value)
        {
            foreach (var name in Enum.GetValues(typeof(KnownColor)))
            {
                var c = Color.FromKnownColor((KnownColor)name);
                if (c.ToArgb() == Value)
                {
                    return c.Name;
                }
            }
            return null;
        }

        public override string ToString()
        {
            var c = GetColor();
            var n = c.IsNamedColor ? c.Name : $"{c.A},{c.R},{c.G},{c.B}";
            return $"Color: " + n;
        }
    }
}
