using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using X360.STFS;
using X360.Other;
using RocksmithToolkitLib.Sng;
using RocksmithToolkitLib.Xml;
using RocksmithToolkitLib.Extensions;
using RocksmithToolkitLib.Sng2014HSL;
using MiscUtil.IO;
using MiscUtil.Conversion;
using System.Windows.Forms;
using System.Text.RegularExpressions;
using RocksmithToolkitLib.DLCPackage.AggregateGraph;

namespace RocksmithToolkitLib.DLCPackage
{
    public static class Packer
    {
        #region FIELDS

        private const string ROOT_XBox360 = "Root";        
        
        #endregion

        #region PACK

        public static void Pack(string sourcePath, string saveFileName, bool updateSng = false, GamePlatform packagePlatform = GamePlatform.None)
        {
            Platform platform = sourcePath.GetPlatform();

            if (packagePlatform != GamePlatform.None) 
                platform.platform = packagePlatform;
            else if (platform.platform == GamePlatform.None && packagePlatform == GamePlatform.None)
                platform = TryGetPlatformByEndName(sourcePath);

            switch (platform.platform) {
                case GamePlatform.Pc:
                case GamePlatform.Mac:
                    if (platform.version == GameVersion.RS2012)
                        PackPC(sourcePath, saveFileName, true, updateSng);
                    else if (platform.version == GameVersion.RS2014)
                        Pack2014(sourcePath, saveFileName, platform, updateSng);
                    break;
                case GamePlatform.XBox360:
                    PackXBox360(sourcePath, saveFileName, platform, updateSng);
                    break;
                case GamePlatform.PS3:
                    PackPS3(sourcePath, saveFileName, platform, updateSng);
                    break;
                case GamePlatform.None:
                    throw new InvalidOperationException(String.Format("Invalid directory structure of package. {0}Directory: {1}", Environment.NewLine, sourcePath));
            }
        }

        #endregion

        #region UNPACK

        public static void Unpack(string sourceFileName, string savePath, GamePlatform packagePlatform = GamePlatform.None)
        {
            Platform platform = sourceFileName.GetPlatform();

            if (packagePlatform != GamePlatform.None)
                platform.platform = packagePlatform;
            else if (platform.platform == GamePlatform.None && packagePlatform == GamePlatform.None)
                platform = TryGetPlatformByEndName(sourceFileName);
                
            var useCryptography = platform.version == GameVersion.RS2012; // Cryptography way is used only for PC in Rocksmith 1

            switch (platform.platform)
            {
                case GamePlatform.Pc:
                case GamePlatform.Mac:
                    if (platform.version == GameVersion.RS2014)
                        using (var inputStream = File.OpenRead(sourceFileName))
                            ExtractPSARC(sourceFileName, savePath, inputStream, platform);
                    else
                    {
                        using (var inputFileStream = File.OpenRead(sourceFileName))
                        using (var inputStream = new MemoryStream())
                        {

                            if (useCryptography)
                                RijndaelEncryptor.DecryptFile(inputFileStream, inputStream, RijndaelEncryptor.DLCKey);
                            else
                                inputFileStream.CopyTo(inputStream);

                            ExtractPSARC(sourceFileName, savePath, inputStream, platform);
                        }
                    }
                    return;
                case GamePlatform.XBox360:
                    UnpackXBox360Package(sourceFileName, savePath, platform);
                    return;
                case GamePlatform.PS3:
                    UnpackPS3Package(sourceFileName, savePath, platform);
                    return;
                case GamePlatform.None:
                    throw new InvalidOperationException("Platform not found :(");
            }
        }

        #endregion

        #region PC 2012

        private static void PackPC(string sourcePath, string saveFileName, bool useCryptography, bool updateSng)
        {
            string[] namesBlock = Directory.GetFiles(sourcePath, "NamesBlock.bin", SearchOption.AllDirectories);
            foreach (var nb in namesBlock) {
                if (File.Exists(nb))
                    File.Delete(nb);
            }

            using (var psarcStream = new MemoryStream())
            using (var streamCollection = new DisposableCollection<Stream>())
            {
                var psarc = new PSARC.PSARC();

                foreach (var x in Directory.EnumerateFiles(sourcePath))
                {
                    var fileStream = File.OpenRead(x);
                    streamCollection.Add(fileStream);
                    var entry = new PSARC.Entry
                    {
                        Name = Path.GetFileName(x),
                        Data = fileStream,
                        Length = (ulong)fileStream.Length
                    };
                    psarc.AddEntry(entry);
                }

                foreach (var directory in Directory.EnumerateDirectories(sourcePath))
                {
                    var innerPsarcStream = new MemoryStream();
                    streamCollection.Add(innerPsarcStream);
                    var directoryName = Path.GetFileName(directory);

                    // Recreate SNG
                    if (updateSng)
                        if (directory.ToLower().IndexOf("dlc_tone_") < 0)
                            UpdateSng(directory, new Platform(GamePlatform.Pc, GameVersion.RS2012));

                    PackInnerPC(innerPsarcStream, directory);
                    psarc.AddEntry(directoryName + ".psarc", innerPsarcStream);
                }

                psarc.Write(psarcStream, false);
                psarcStream.Flush();
                psarcStream.Seek(0, SeekOrigin.Begin);

                if (Path.GetExtension(saveFileName) != ".dat")
                    saveFileName += ".dat";

                using (var outputFileStream = File.Create(saveFileName))
                {
                    if (useCryptography)
                        RijndaelEncryptor.EncryptFile(psarcStream, outputFileStream, RijndaelEncryptor.DLCKey);
                    else
                        psarcStream.CopyTo(outputFileStream);
                }
            }
        }

        private static void PackInnerPC(Stream output, string directory)
        {
            using (var streamCollection = new DisposableCollection<Stream>())
            {
                var innerPsarc = new PSARC.PSARC();
                WalkThroughDirectory("", directory, (a, b) =>
                {
                    var fileStream = File.OpenRead(b);
                    streamCollection.Add(fileStream);
                    innerPsarc.AddEntry(a, fileStream);
                });
                innerPsarc.Write(output, false);
            }
        }

        #endregion

        #region PC/MAC 2014

        private static void Pack2014(string sourcePath, string saveFileName, Platform platform, bool updateSng)
        {
            using (var psarcStream = new MemoryStream())
            {
                var psarc = new PSARC.PSARC();
                if (updateSng) UpdateSng2014(sourcePath, platform);
                WalkThroughDirectory("", sourcePath, (a, b) =>
                {
                    var fileStream = File.OpenRead(b);
                    psarc.AddEntry(a, fileStream);
                });

                psarc.Write(psarcStream, platform.IsConsole ? false : true);
                psarcStream.Flush();
                psarcStream.Seek(0, SeekOrigin.Begin);
                
                if (Path.GetExtension(saveFileName) != ".psarc")
                    saveFileName += ".psarc";

                using (var outputFileStream = File.Create(saveFileName))
                    psarcStream.CopyTo(outputFileStream);

                foreach (var entry in psarc.Entries)
                    entry.Data.Close();
            }
        }

        #endregion

        #region XBox 360

        private static void PackXBox360(string sourcePath, string saveFileName, Platform platform, bool updateSng) {
            if (updateSng && platform.version == GameVersion.RS2014)
                UpdateSng2014(sourcePath, platform);

            foreach (var directory in Directory.EnumerateDirectories(Path.Combine(sourcePath, ROOT_XBox360))) {
                PackInnerXBox360(Path.Combine(sourcePath, ROOT_XBox360), directory);
            }

            IEnumerable<string> xboxHeaderFiles = Directory.EnumerateFiles(sourcePath, "*.txt");
            DLCPackageData songData = new DLCPackageData();
            foreach (var file in xboxHeaderFiles) {
                if (xboxHeaderFiles.Count() == 1)
                {
                    try
                    {
                        string[] xboxHeader = File.ReadAllLines(file);
                        if (xboxHeader != null && xboxHeader.Length > 73)
                        {
                            if (xboxHeader[0].IndexOf("LIVE") > 0)
                            {
                                songData.SignatureType = PackageMagic.LIVE;

                                for (int i = 2; i <= 48; i = i + 3)
                                {
                                    long id = Convert.ToInt64(xboxHeader[i].GetHeaderValue(), 16);
                                    int bit = Convert.ToInt32(xboxHeader[i + 1].GetHeaderValue());
                                    int flag = Convert.ToInt32(xboxHeader[i + 2].GetHeaderValue());

                                    if (id != 0)
                                        songData.XBox360Licenses.Add(new XBox360License() { ID = id, Bit = bit, Flag = flag });
                                }
                            }
                            
                            string songInfo = xboxHeader[74];
                            
                            int index = songInfo.IndexOf(" by ");
                            string songTitle = (index > 0) ? songInfo.Substring(0, index) : songInfo;
                            string songArtist = (index > 4) ? songInfo.Substring(index + 4) : songInfo;
                                                        
                            if (!String.IsNullOrEmpty(songInfo))
                            {
                                songData.SongInfo = new SongInfo();
                                songData.SongInfo.SongDisplayName = songInfo;
                                songData.SongInfo.Artist = songInfo;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidDataException("XBox360 header file (.txt) not found or is invalid. \nThe file is in the same level at 'Root' folder along with the files: 'Content image.png' and 'Package image.png' and no other file .txt can be here.", ex);
                    }
                }
                else
                {
                    throw new InvalidDataException("XBox360 header file (.txt) not found or is invalid. The file is in the same level at 'Root' folder along with the files: 'Content image.png' and 'Package image.png'. No other file .txt can be here.");
                }
            }

            IEnumerable<string> xboxFiles = Directory.EnumerateFiles(Path.Combine(sourcePath, ROOT_XBox360));
            DLCPackageCreator.BuildXBox360Package(saveFileName, songData, xboxFiles, platform.version);

            foreach (var file in xboxFiles)
                if (Path.GetExtension(file) == ".psarc" && File.Exists(file))
                    File.Delete(file);
        }

        private static void PackInnerXBox360(string sourcePath, string directory)
        {
            using (var psarcStream = new MemoryStream())
            {
                var innerPsarc = new PSARC.PSARC();

                WalkThroughDirectory("", directory, (a, b) =>
                {
                    var fileStream = File.OpenRead(b);
                    innerPsarc.AddEntry(a, fileStream);
                });

                innerPsarc.Write(psarcStream, false);
                psarcStream.Flush();
                psarcStream.Seek(0, SeekOrigin.Begin);

                using (var outputFileStream = File.Create(Path.Combine(sourcePath, Path.GetFileName(directory)) + ".psarc"))
                {
                    psarcStream.CopyTo(outputFileStream);
                }
            }
        }

        private static void UnpackXBox360Package(string sourceFileName, string savePath, Platform platform)
        {
            LogRecord x = new LogRecord();
            STFSPackage xboxPackage = new STFSPackage(sourceFileName, x);
            if (!xboxPackage.ParseSuccess)
                throw new InvalidDataException("Invalid Rocksmith XBox 360 package!\n" + x.Log);

            var rootDir = Path.Combine(savePath, Path.GetFileNameWithoutExtension(sourceFileName)) + String.Format("_{0}", platform.platform.ToString());
            xboxPackage.ExtractPayload(rootDir, true, true);

            foreach (var fileName in Directory.EnumerateFiles(Path.Combine(rootDir, "Root")))
            {
                if (Path.GetExtension(fileName) == ".psarc")
                {
                    using (var outputFileStream = File.OpenRead(fileName))
                    {
                        ExtractPSARC(fileName, Path.GetDirectoryName(fileName), outputFileStream, new Platform(GamePlatform.XBox360, GameVersion.None));
                    }
                }

                if (File.Exists(fileName) && Path.GetExtension(fileName) == ".psarc")
                    File.Delete(fileName);
            }

            xboxPackage.CloseIO();
        }

        private static string GetHeaderValue(this string value)
        {
            return value.Substring(value.IndexOf(":") + 2);
        }

        #endregion

        #region PS3

        private static void PackPS3(string sourcePath, string saveFileName, Platform platform, bool updateSng) {
            Pack2014(sourcePath, saveFileName, platform, updateSng);

            var edatDir = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "edat");
            if (!Directory.Exists(edatDir))
                Directory.CreateDirectory(edatDir);

            var sourceCleanPackage = saveFileName + ".psarc";
            var destCleanPackage = Path.Combine(edatDir, Path.GetFileName(saveFileName) + ".psarc");
            var encryptedPackage = destCleanPackage + ".edat";

            if (File.Exists(sourceCleanPackage))
                File.Move(sourceCleanPackage, destCleanPackage);

            var outputMessage = RijndaelEncryptor.EncryptPS3Edat();

            if (outputMessage.IndexOf("Encrypt all EDAT files successfully") > 0) {
                if (File.Exists(destCleanPackage))
                    File.Delete(destCleanPackage);

                if (File.Exists(encryptedPackage))
                    File.Move(encryptedPackage, sourceCleanPackage + ".edat");
            }
        }

        private static void UnpackPS3Package(string sourceFileName, string savePath, Platform platform)
        {
            var rootDir = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "edat");
            var outputFilename = Path.Combine(rootDir, Path.GetFileName(sourceFileName));

            if (!Directory.Exists(rootDir))
                Directory.CreateDirectory(rootDir);

            if (File.Exists(sourceFileName))
                File.Copy(sourceFileName, outputFilename, true);
            else
                throw new FileNotFoundException(String.Format("File '{0}' not found.", sourceFileName));

            var outputMessage = RijndaelEncryptor.DecryptPS3Edat();

            if (File.Exists(outputFilename))
                File.Delete(outputFilename);

            foreach (var fileName in Directory.EnumerateFiles(rootDir, "*.psarc.dat"))
            {
                using (var outputFileStream = File.OpenRead(fileName))
                {
                    ExtractPSARC(fileName, Path.GetDirectoryName(fileName), outputFileStream, new Platform(GamePlatform.PS3, GameVersion.None));
                }

                if (File.Exists(fileName))
                    File.Delete(fileName);
            }

            var outName = Path.GetFileNameWithoutExtension(sourceFileName);
            var outputDir = Path.Combine(savePath, outName.Substring(0, outName.LastIndexOf(".")) + String.Format("_{0}", platform.platform.ToString()));

            foreach (var unpackedDir in Directory.EnumerateDirectories(rootDir))
                if (Directory.Exists(unpackedDir))
                    Directory.Move(unpackedDir, outputDir);

            if (outputMessage.IndexOf("Decrypt all EDAT files successfully") < 0)
                throw new InvalidOperationException("Rebuilder error, please check if .edat files are created correctly and see output below:" + Environment.NewLine + Environment.NewLine + outputMessage);
        }

        #endregion

        #region COMMON FUNCTIONS

        public static void DeleteFixedAudio(string sourcePath)
        {
        	try {
            foreach (var file in Directory.GetFiles(sourcePath, "*_fixed.*", SearchOption.AllDirectories).Where(s => s.EndsWith(".ogg") || s.EndsWith(".wem")))
            	if (File.Exists(file)) File.Delete(file);
        	}
        	catch (Exception ex){ throw new InvalidOperationException(String.Format("Can't delete garbage Audio files!\r\n {0}", ex)); }
        }

        private static void WalkThroughDirectory(string baseDir, string directory, Action<string, string> action) {
            foreach (var fl in Directory.GetFiles(directory))
                action(String.Format("{0}/{1}", baseDir, Path.GetFileName(fl)).TrimStart('/'), fl);
            foreach (var dr in Directory.GetDirectories(Path.Combine(baseDir, directory)))
                WalkThroughDirectory(String.Format("{0}/{1}", baseDir, Path.GetFileName(dr)), dr, action);
        }

        //TODO: validate Files by MIME type.
        public static Platform GetPlatform(this string fullPath) {
            if (File.Exists(fullPath)) {
                // Get PLATFORM by Extension + Get PLATFORM by pkg EndName
                switch (Path.GetExtension(fullPath)) {
                    case ".dat":
                        return new Platform(GamePlatform.Pc, GameVersion.RS2012);
                    case "":
                        return new Platform(GamePlatform.XBox360, GameVersion.RS2012);
                    case ".edat":
                        return new Platform(GamePlatform.PS3, GameVersion.None);
                    case ".psarc":
                        return TryGetPlatformByEndName(fullPath);
                    default:
                        return new Platform(GamePlatform.None, GameVersion.None);
                }
            }
            else if (Directory.Exists(fullPath))
            {
                // GET PLATFORM BY PACKAGE ROOT DIRECTORY
                if (File.Exists(Path.Combine(fullPath, "APP_ID"))) {
                    // PC 2012
                    return new Platform(GamePlatform.Pc, GameVersion.RS2012);
                } else if (File.Exists(Path.Combine(fullPath, "appid.appid"))) {
                    // PC / MAC 2014
                    var agg = Directory.GetFiles(fullPath, "*.nt", SearchOption.TopDirectoryOnly)[0];
                    var aggContent = File.ReadAllText(agg);

                    if (aggContent.Contains("\"dx9\""))
                        return new Platform(GamePlatform.Pc, GameVersion.RS2014);
                    else if (aggContent.Contains("\"macos\""))
                        return new Platform(GamePlatform.Mac, GameVersion.RS2014);
                    else
                        return new Platform(GamePlatform.Pc, GameVersion.None);
                } else if (Directory.Exists(Path.Combine(fullPath, ROOT_XBox360))) {
                    // XBOX 2012/2014
                    var hTxt = Directory.GetFiles(fullPath, "*.txt", SearchOption.TopDirectoryOnly)[0];
                    var hTxtContent = File.ReadAllText(hTxt);

                    if (hTxtContent.Contains("Title ID: 55530873"))
                        return new Platform(GamePlatform.XBox360, GameVersion.RS2012);
                    else if (hTxtContent.Contains("Title ID: 555308C0"))
                        return new Platform(GamePlatform.XBox360, GameVersion.RS2014);
                    else
                        return new Platform(GamePlatform.XBox360, GameVersion.None);
                } else {
                    // PS3 2012/2014
                    var agg = Directory.GetFiles(fullPath, "*.nt", SearchOption.TopDirectoryOnly);

                    if (agg.Length > 0) {
                        var aggContent = File.ReadAllText(agg[0]);

                        if (aggContent.Contains("\"PS3\""))
                            return new Platform(GamePlatform.PS3, GameVersion.RS2012);
                        else if (aggContent.Contains("\"ps3\""))
                            return new Platform(GamePlatform.PS3, GameVersion.RS2014);
                        else
                            return TryGetPlatformByEndName(fullPath);
                    }
                    else
                        return new Platform(GamePlatform.None, GameVersion.None);
                } 
            } else
                return new Platform(GamePlatform.None, GameVersion.None);
        }

		/// <summary>
		/// Gets platform from name ending
		/// </summary>
		/// <param name="fileName">Folder of File</param>
		/// <returns>Platform(DetectedPlatform, RS2014 ? None)</returns>
        private static Platform TryGetPlatformByEndName(string fileName)
        {
            GamePlatform p = GamePlatform.None;
            GameVersion v = GameVersion.RS2014;
            var pIndex = Path.GetFileNameWithoutExtension(fileName).LastIndexOf("_");

            if (Directory.Exists(fileName))
            {// Pc, Mac, XBox360, PS3
                string platformString = Path.GetFileNameWithoutExtension(fileName).Substring(pIndex+1);
                bool isValid = Enum.TryParse(platformString, true, out p);
                if (isValid) return new Platform(p, v);
                else return new Platform(GamePlatform.None, GameVersion.None);
            }
            else
            {//_p, _m, _ps3, _xbox
                string platformString = pIndex > -1 ? Path.GetFileNameWithoutExtension(fileName).Substring(pIndex) : "";
                switch (platformString.ToLower()) {
                    case "_p":
                        return new Platform(GamePlatform.Pc, v);
                    case "_m":
                        return new Platform(GamePlatform.Mac, v);
                    case "_ps3":
                        return new Platform(GamePlatform.PS3, v);
                    case "_xbox":
                        return new Platform(GamePlatform.XBox360, v);
                    default:
                        return new Platform(GamePlatform.Pc, v);
                }
            } 
        }

        private static void ExtractPSARC(string filename, string path, Stream inputStream, Platform platform, bool isExternalFile = true)
        {
            string name = Path.GetFileNameWithoutExtension(filename);

            if (isExternalFile)
                name += String.Format("_{0}", platform.platform.ToString());

            var psarc = new PSARC.PSARC();
            psarc.Read(inputStream);
            foreach (var entry in psarc.Entries)
            {
                var fullfilename = Path.Combine(path, name, entry.Name);
                entry.Data.Seek(0, SeekOrigin.Begin);
                if (Path.GetExtension(entry.Name).ToLower() == ".psarc")
                {
                    ExtractPSARC(fullfilename, Path.Combine(path, name), entry.Data, platform, false);
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(fullfilename));
                    using (var fileStream = File.Create(fullfilename))
                    {
                        entry.Data.CopyTo(fileStream);
                        entry.Data.Seek(0, SeekOrigin.Begin);
                        entry.Data.Close(); //allow tmp file to be deleted.
                    }
                }
            }
        }

        private static void UpdateSng(string songDirectory, Platform platform) {
            var xmlFiles = Directory.EnumerateFiles(Path.Combine(songDirectory, @"GR\Behaviors\Songs"));

            foreach (var xmlFile in xmlFiles) {
                if (File.Exists(xmlFile) && Path.GetExtension(xmlFile) == ".xml") {
                    var sngFile = Path.Combine(songDirectory, "GRExports", platform.GetPathName()[1], Path.GetFileNameWithoutExtension(xmlFile) + ".sng");
                    var arrType = ArrangementType.Guitar;

                    if (Path.GetFileName(xmlFile).ToLower().IndexOf("vocal") >= 0) {
                        arrType = ArrangementType.Vocal;
                        SngFileWriter.Write(xmlFile, sngFile, arrType, platform);
                    } else {
                        Song song = Song.LoadFromFile(xmlFile);

                        if (!Enum.TryParse<ArrangementType>(song.Arrangement, out arrType))
                            if (song.Arrangement.ToLower().IndexOf("bass") >= 0)
                                arrType = ArrangementType.Bass;
                    }

                    SngFileWriter.Write(xmlFile, sngFile, arrType, platform);
                } else {
                    throw new ArgumentException(String.Format("'{0}' is not a valid XML file.", xmlFile));
                }
            }
        }

        private static void UpdateSng2014(string songDirectory, Platform platform) {
            var xmlFiles = Directory.GetFiles(Path.Combine(songDirectory, "songs", "arr"), "*_*.xml", SearchOption.AllDirectories);

            foreach (var xmlFile in xmlFiles)
            {
                if (File.Exists(xmlFile) && !(xmlFile.ToLower().Contains("showlight")))
                {
                    var sngFile = Path.Combine(songDirectory, Path.Combine("songs","bin", platform.GetPathName()[1]), Path.GetFileNameWithoutExtension(xmlFile) + ".sng");
                    var arrType = ArrangementType.Guitar;
                    
                    if (Path.GetFileName(xmlFile).ToLower().IndexOf("vocal") >= 0)
                        arrType = ArrangementType.Vocal;

                    using (FileStream fs = new FileStream(sngFile, FileMode.Create)) {
                        Sng2014File sng = Sng2014File.ConvertXML(xmlFile, arrType);
                        sng.writeSng(fs, platform);
                    }
                }
                if (File.Exists(xmlFile) && (xmlFile.ToLower().Contains("showlight")))
                {
                    var shl = new RocksmithToolkitLib.DLCPackage.Showlight.Showlights();
                    shl.ShowlightList = shl.FixShowlights(RocksmithToolkitLib.DLCPackage.Showlight.Showlights.LoadFromFile(xmlFile).ShowlightList);
                    shl.Count = shl.ShowlightList.Count;
                    using (var fs = new FileStream(xmlFile, FileMode.Create))
                    {
                        shl.Serialize(fs);
                    }
                }
            }
        }

        #endregion
    }
}
