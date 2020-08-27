using CommandLine;
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace PackageForRelease
{
    class Program
    {
        public class Options
        {
            [Option(HelpText = "The output directory holding compiled plugin binaries.", Required = true)]
            public string BinDir { get; set; }

            [Option(HelpText = "The directory where the published package should be stored.", Required = true)]
            public string PubDir { get; set; }

            [Option(HelpText = "The plugin short name. This value is used in naming the output package.", Required = true)]
            public string ShortName { get; set; }

            [Option(HelpText = "The plugin full name. This name will be displayed in the repo browser.", Required = true)]
            public string FullName { get; set; }

            [Option(HelpText = "The plugin version.", Required = true)]
            public string Version { get; set; }
        }

        static void Main(string[] args)
        {
            Parser.Default.ParseArguments<Options>(args)
                .WithParsed<Options>(o =>
                {
                    Console.WriteLine("GroupMe Desktop Client Plugin Packaging Tool");
                    Console.WriteLine($"-Plugin Binary Directory is {o.BinDir}");
                    Console.WriteLine($"-Publish Directory is {o.PubDir}");
                    Console.WriteLine($"-Plugin Version is {o.Version}");

                    if (!Directory.Exists(o.BinDir))
                    {
                        Console.WriteLine("ERROR: Invalid binary directory specified.");
                        return;
                    }

                    if (!Version.TryParse(o.Version, out var version))
                    {
                        Console.WriteLine("ERROR: Invalid version number.");
                        return;
                    }

                    Directory.CreateDirectory(o.PubDir);

                    var invalidChars = Path.GetInvalidFileNameChars();
                    var safeShortName = string.Join(string.Empty, o.ShortName.Select(c => !invalidChars.Contains(c) ? c : '_'));

                    var packageName = $"{safeShortName}-{version}.zip";
                    var outputFileName = Path.Combine(o.PubDir, packageName);
                    var releasesFileName = Path.Combine(o.PubDir, "RELEASES.txt");

                    File.Delete(outputFileName);

                    using (var outputFile = File.OpenWrite(outputFileName))
                    {
                        using (var archive = new ZipArchive(outputFile, ZipArchiveMode.Create))
                        {
                            foreach (var file in Directory.GetFiles(o.BinDir))
                            {
                                archive.CreateEntryFromFile(file, Path.GetFileName(file));
                            }
                        }
                    }


                    const int MaxReleaseFileAttempts = 10;
                    int attempts = 1;

                    while (attempts < MaxReleaseFileAttempts)
                    {
                        try
                        {
                            string[] releasesFileLines = { };
                            if (File.Exists(releasesFileName))
                            {
                                releasesFileLines = File.ReadAllLines(releasesFileName);
                            }

                            using (var releasesFile = File.Open(releasesFileName, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None))
                            {
                                releasesFile.SetLength(0);
                                bool updatedExistingEntry = false;

                                using (var writer = new StreamWriter(releasesFile))
                                {
                                    foreach (var line in releasesFileLines)
                                    {
                                        var parts = line.Split(new char[] { ' ' }, 3);
                                        var lineFileName = parts[1];
                                        var lineFriendlyName = parts[2];

                                        if (lineFriendlyName == o.FullName && lineFileName.StartsWith(safeShortName))
                                        {
                                            // This entry is for the same package
                                            // Insert the new entry instead
                                            writer.WriteLine($"{Sha1Hash(outputFileName)} {packageName} {o.FullName}");
                                            updatedExistingEntry = true;
                                        }
                                        else
                                        {
                                            // This is another entry so leave it alone
                                            writer.WriteLine(line);
                                        }
                                    }

                                    if (!updatedExistingEntry)
                                    {
                                        // Insert the new package as a first-time entry
                                        writer.WriteLine($"{Sha1Hash(outputFileName)} {packageName} {o.FullName}");
                                    }
                                }
                                break;
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("ERROR: Could not amend RELEASES file " + ex.Message);
                            attempts++;
                        }
                    }
                });
        }

        private static string Sha1Hash(string fileLocation)
        {
            using (FileStream fs = new FileStream(fileLocation, FileMode.Open))
            using (BufferedStream bs = new BufferedStream(fs))
            {
                using (SHA1Managed sha1 = new SHA1Managed())
                {
                    byte[] hash = sha1.ComputeHash(bs);
                    StringBuilder formatted = new StringBuilder(2 * hash.Length);
                    foreach (byte b in hash)
                    {
                        formatted.AppendFormat("{0:X2}", b);
                    }

                    return formatted.ToString();
                }
            }
        }
    }
}
