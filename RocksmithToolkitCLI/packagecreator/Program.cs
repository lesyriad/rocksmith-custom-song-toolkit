﻿using System;
using System.IO;
using NDesk.Options;
using RocksmithToolkitLib.DLCPackage;
using RocksmithToolkitLib.Extensions;
using RocksmithToolkitLib.Sng;
using RocksmithToolkitLib;

namespace packagecreator
{
    // TODO: include functionality for RS2012
    internal class Arguments
    {
        public bool ShowHelp;
        public bool Package;
        public string[] Input;
        public string Output;
        public Platform Platform;
        public string AppId;
        public string Revision;
        public string Quality;
        public string Decibels;
    }

    static class Program
    {
        private static Arguments DefaultArguments()
        {
            return new Arguments
            {
                Package = true,
                Platform = new Platform(GamePlatform.Pc, GameVersion.RS2014),
                AppId = "248750",  // RS1 => 206102 not currently supported
                Revision = "1",
                Quality = "4", // not currently used
                Decibels = "-12"
            };
        }

        private static OptionSet GetOptions(Arguments outputArguments)
        {
            return new OptionSet
            {
                { "h|?|help", "Show this help message and exit\r\n", v => outputArguments.ShowHelp = v != null },
                { "p|package", "Usage: Drag/Drop a directory " +
                  "that contains the following CDLC ready files onto the executable:\r\n" +
                  "Song2014.xml [lead, rhythm, combo, and/or bass]\r\n"+
                  "Song2014.json [arrangments] (optional)\r\n"+
                  "Vocals.xml (optional)\r\nAlbumArt256.dds\r\nAudio.wem\r\nAudio_preview.wem (optional, recommended)\r\n", v => { if (v != null) outputArguments.Package = true; }},                
                { "i|input=", "The input directory (multiple allowed, use ; to split paths)", v => outputArguments.Input = v.Split( new[]{';'}, 2) },
                { "o|output=", "The output directory (defualt = input directory)", v => outputArguments.Output = v },
                { "f|platform=", "Platform to pack package [Pc, Mac, XBox360, PS3] (default = Pc)", v => outputArguments.SetPlatform(v) },
                { "v|version=", "Version of the Rocksmith Game [RS2012 or RS2014] (defualt = RS2014)", v => outputArguments.SetVersion(v) },
                { "a|appid=", "App ID of the Rocksmith DLC (defualt = RS2014)", v => { if (v != null) outputArguments.AppId = v; }},
                { "r|revision=", "Revision of the CDLC package [1, 2.3] (default = 1)", v => { if (v != null) outputArguments.Revision = v; }},
                { "q|quality=", "Quality of audio  [4 to 9] (defualt = 4)", v => { if (v != null) outputArguments.Quality = v; }},
                { "d|decibels=", "Audio volume in decibels [HIGHER -1, AVERAGE -12, -16 LOWER] (default = -12)", v => outputArguments.Output = v }
            };
        }

        private static void SetPlatform(this Arguments arguments, string platformString)
        {
            GamePlatform p;
            var validPlatform = Enum.TryParse(platformString, true, out p);
            if (!validPlatform)
            {
                ShowHelpfulError(String.Format("{0} is not a valid platform.", platformString));
                arguments.Platform.platform = GamePlatform.None;
            }
            arguments.Platform.platform = p;
        }

        private static void SetVersion(this Arguments arguments, string versionString)
        {
            GameVersion v;
            var validVersion = Enum.TryParse(versionString, true, out v);
            if (!validVersion)
            {
                ShowHelpfulError(String.Format("{0} is not a valid game version.", versionString));
                arguments.Platform.version = GameVersion.None;
            }
            arguments.Platform.version = v;
        }

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static int Main(string[] args)
        {
            Console.WindowWidth = 85;
            Console.BackgroundColor = ConsoleColor.Black;
            Console.ForegroundColor = ConsoleColor.Green;

#if (DEBUG)
            // give the progie some dumby directory to work on for testing
            args = new string[] { "--input=D:\\Temp\\Test", "--output=D:\\Temp" }; //, "platform=Pc", "version=RS2014" };
           // args = new string[] { "D:\\Temp\\Test" };
#endif

            var arguments = DefaultArguments();
            var options = GetOptions(arguments);
            string[] srcDirs = null;
            options.Parse(args);

            try
            {
                // drag/drop a directory onto executable application
                if (arguments.Input == null && args.GetLength(0) != 0)
                {
                    try
                    {
                        if (args[0].IsDirectory())
                        {
                            srcDirs = args;
                            arguments.Output = Path.GetDirectoryName(args[0]);
                        }
                    }
                    catch (Exception ex)
                    {
                        ShowHelpfulError("Fatal Error: " + ex.Message);
                        return 1; // failure
                    }
                }
                else // command line error checking 
                {
                    if (arguments.ShowHelp || args.GetLength(0) == 0)
                    {
                        options.WriteOptionDescriptions(Console.Out);
                        Console.ReadLine();
                        return -1; // neither success or failure
                    }

                    if (!arguments.Package)
                    {
                        ShowHelpfulError("Must specify the primary command as 'package'");
                        return 1;
                    }

                    if (arguments.Package)
                    {
                        if (!arguments.Input[0].IsDirectory() || (arguments.Input == null && arguments.Input.Length <= 0))
                        {
                            ShowHelpfulError("Must specify and 'input' directory.");
                            return 1;
                        }

                        if (string.IsNullOrEmpty(arguments.Output))
                        {
                            ShowHelpfulError("Must specify an 'output' directory.");
                            return 1;
                        }
                    }

                    if ((arguments.Platform.platform == GamePlatform.None && arguments.Platform.version != GameVersion.None) || (arguments.Platform.platform != GamePlatform.None && arguments.Platform.version == GameVersion.None))
                    {
                        ShowHelpfulError("'platform' argument requires 'version' and vice-versa to define platform.\r\nUse this option only if you have problem with platform auto identifier");
                        return 1;
                    }

                    srcDirs = arguments.Input;
                }

                Console.WriteLine(@"Initializing Package Creator CLI ...");
                Console.WriteLine("");

                foreach (string srcDir in srcDirs)
                {
                    Console.WriteLine(@"Parsing CDLC Package Data from Input Directory: " + Path.GetFileName(srcDir));
                    try
                    {
                        // get package data
                        DLCPackageData packageData = DLCPackageData.LoadFromFolder(srcDir, arguments.Platform, arguments.Platform);
                        packageData.AppId = arguments.AppId;
                        packageData.PackageVersion = arguments.Revision;
                        packageData.Name = Path.GetFileName(srcDir).GetValidName();
                        packageData.Volume = packageData.Volume == 0 ? Convert.ToInt16(arguments.Decibels) : packageData.Volume;
                        packageData.PreviewVolume = packageData.PreviewVolume == 0 ? Convert.ToInt16(arguments.Decibels) : packageData.PreviewVolume;

                        // convert combo arrangements to rhythm or lead so game recognizes each properly
                        var comboCount = 1;
                        for (int arrIndex = 0; arrIndex < packageData.Arrangements.Count; arrIndex++)
                        {
                            if (packageData.Arrangements[arrIndex].Name == ArrangementName.Combo)
                            {
                                if (comboCount == 1)
                                    packageData.Arrangements[arrIndex].Name = ArrangementName.Rhythm;
                                if (comboCount == 2)
                                    packageData.Arrangements[arrIndex].Name = ArrangementName.Lead;
                                if (comboCount > 2)
                                    throw new Exception("Too many Combo arrangements");
                                comboCount++;
                            }
                        }

                        // generate CDLC file name
                        var artist = packageData.SongInfo.ArtistSort;
                        var title = packageData.SongInfo.SongDisplayNameSort;
                        // var destDir = Path.Combine(arguments.Output, Path.GetFileName(srcDir).GetValidName());
                        var fileName = GeneralExtensions.GetShortName("{0}_{1}_v{2}", artist, title, arguments.Revision.Replace(".", "_"), ConfigRepository.Instance().GetBoolean("creator_useacronyms"));
                        var destPath = Path.Combine(arguments.Output, fileName);
                        var fullFileName = String.Format("{0}{1}.psarc", fileName, DLCPackageCreator.GetPathName(arguments.Platform)[2]);
                        Console.WriteLine(@"Packing: " + Path.GetFileName(fullFileName));
                        Console.WriteLine("");
                        // pack the data
                        DLCPackageCreator.Generate(destPath, packageData, new Platform(arguments.Platform.platform, arguments.Platform.version));
                        packageData.CleanCache();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("");
                        Console.WriteLine(String.Format("Packaging error!\nDirectory: {0}\n{1}\n{2}", srcDir, ex.Message, ex.InnerException));
                        Console.ReadLine();
                    }
                }

                Console.WriteLine(@"All Finished");
                Console.WriteLine(@"Press any key to continue ...");
                Console.ReadLine();
                return 0; // success
            }
            catch (OptionException ex)
            {
                ShowHelpfulError(ex.Message);
                return 1; // failure
            }
        }

        static void ShowHelpfulError(string message)
        {
            Console.Write("packagecreator: ");
            Console.WriteLine(message);
            Console.WriteLine("Try 'packagecreator --help' for more information.");
            Console.ReadLine();
        }

        private static bool IsDirectory(this string path)
        {
            bool isDirectory = false;

            try
            {
                FileAttributes attr = File.GetAttributes(path);
                if ((attr & FileAttributes.Directory) == FileAttributes.Directory)
                    isDirectory = true;
            }
            catch (Exception ex)
            {
                ShowHelpfulError("Invalid directory." + Environment.NewLine + ex.Message);
            }

            return isDirectory;
        }

    }
}