﻿using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;

using Ionic.Zip;

using SaintCoinach.Ex;
using SaintCoinach.Ex.Relational.Definition;
using SaintCoinach.Ex.Relational.Update;
using SaintCoinach.IO;
using SaintCoinach.Xiv;

using YamlDotNet.Serialization;

using Directory = System.IO.Directory;
using File = System.IO.File;

namespace SaintCoinach {
    /// <summary>
    ///     Central class for accessing game assets.
    /// </summary>
    public class ARealmReversed {
        #region Static

        /// <summary>
        ///     Default file name of the archive containing current and past data mappings.
        /// </summary>
        private const string DefaultStateFile = "SaintCoinach.History.zip";

        /// <summary>
        ///     File name inside the archive of the data mappings.
        /// </summary>
        private const string DefinitionFile = "ex.yaml";

        /// <summary>
        ///     File name containing the current version string.
        /// </summary>
        private const string VersionFile = "ffxivgame.ver";

        /// <summary>
        ///     Format string to create the file name for update reports in text form. {0} is the previous and {1} the new version.
        /// </summary>
        private const string UpdateReportTextFile = "logs/report-{0}-{1}.log";

        /// <summary>
        ///     Format string to create the file name for update reports in YAML form. {0} is the previous and {1} the new version.
        /// </summary>
        private const string UpdateReportYamlFile = "logs/report-{0}-{1}.yaml";

        /// <summary>
        ///     Format string to create the file name for update reports in binary form. {0} is the previous and {1} the new
        ///     version.
        /// </summary>
        private const string UpdateReportBinFile = "logs/report-{0}-{1}.bin";

        /// <summary>
        ///     <see cref="Encoding" /> to use inside the <see cref="ZipFile" />.
        /// </summary>
        private static readonly Encoding ZipEncoding = Encoding.UTF8;

        #endregion

        #region Fields

        /// <summary>
        ///     Game data collection for the data files.
        /// </summary>
        private readonly XivCollection _GameData;

        /// <summary>
        ///     Root directory of the game installation.
        /// </summary>
        private readonly DirectoryInfo _GameDirectory;

        /// <summary>
        ///     Version of the game data.
        /// </summary>
        private readonly string _GameVersion;

        /// <summary>
        ///     Pack collection for the data files.
        /// </summary>
        private readonly PackCollection _Packs;

        /// <summary>
        ///     Archive file containing current and past data mappings. 
        /// </summary>
        private readonly FileInfo _StateFile;

        #endregion

        #region Properties

        /// <summary>
        ///     Gets the root directory of the game installation.
        /// </summary>
        /// <value>The root directory of the game installation.</value>
        public DirectoryInfo GameDirectory { get { return _GameDirectory; } }

        /// <summary>
        ///     Gets the pack collection for the data files.
        /// </summary>
        /// <value>The pack collection for the data files.</value>
        public PackCollection Packs { get { return _Packs; } }

        /// <summary>
        ///     Gets the game data collection for the data files.
        /// </summary>
        /// <value>The game data collection for the data files.</value>
        public XivCollection GameData { get { return _GameData; } }

        /// <summary>
        ///     Gets the version of the game data.
        /// </summary>
        /// <value>The version of the game data.</value>
        public string GameVersion { get { return _GameVersion; } }

        /// <summary>
        ///     Gets the version of the loaded definition.
        /// </summary>
        /// <value>The version of the loaded definition.</value>
        public string DefinitionVersion { get { return GameData.Definition.Version; } }

        /// <summary>
        ///     Gets a value indicating whether the loaded definition is the same as the game data version.
        /// </summary>
        /// <value>Whether the loaded definition is the same as the game data version.</value>
        public bool IsCurrentVersion { get { return GameVersion == DefinitionVersion; } }

        /// <summary>
        ///     Gets the archive file containing current and past data mappings.
        /// </summary>
        /// <value>The archive file containing current and past data mappings.</value>
        public FileInfo StateFile { get { return _StateFile; } }

        #endregion

        #region Setup

        /// <summary>
        ///     Perform first-time setup on the archive.
        /// </summary>
        /// <param name="zip"><see cref="ZipFile" /> used for storage.</param>
        /// <returns>Returns the initial <see cref="RelationDefinition" /> object.</returns>
        private RelationDefinition Setup(ZipFile zip) {
            RelationDefinition fsDef = null, zipDef = null;
            DateTime fsMod = DateTime.MinValue, zipMod = DateTime.MinValue;

            if (!TryGetDefinitionFromFileSystem(out fsDef, out fsMod))
                fsDef = null;
            
            if (zip.ContainsEntry(DefinitionFile))
                zipDef = ReadDefinition(zip, DefinitionFile, out zipMod);

            if (fsDef == null && zipDef == null)
                throw new InvalidOperationException();

            RelationDefinition def;
            if (fsMod > zipMod)
                def = fsDef;
            else
                def = zipDef;

            if (def.Version != GameVersion)
                System.Diagnostics.Trace.WriteLine(string.Format("Definition and game version mismatch ({0} != {1})", def.Version, GameVersion));

            def.Version = GameVersion;
            StoreDefinition(zip, def, string.Format("{0}/{1}", def.Version, DefinitionFile));
            StoreDefinition(zip, def, DefinitionFile);
            StorePacks(zip);
            UpdateVersion(zip);

            zip.Save();

            return def;
        }

        #endregion

        #region Constructors

        /// <summary>
        ///     Initializes a new instance of the <see cref="ARealmReversed" /> class.
        /// </summary>
        /// <param name="gamePath">Directory path to the game installation.</param>
        /// <param name="language">Initial language to use.</param>
        public ARealmReversed(string gamePath, Language language) : this(new DirectoryInfo(gamePath), new FileInfo(DefaultStateFile), language, null) { }

        /// <summary>
        ///     Initializes a new instance of the <see cref="ARealmReversed" /> class.
        /// </summary>
        /// <param name="gamePath">Directory path to the game installation.</param>
        /// <param name="storePath">Path to the file used for storing definitions and history.</param>
        /// <param name="language">Initial language to use.</param>
        public ARealmReversed(string gamePath, string storePath, Language language) : this(new DirectoryInfo(gamePath), new FileInfo(storePath), language, null) { }

        /// <summary>
        ///     Initializes a new instance of the <see cref="ARealmReversed" /> class.
        /// </summary>
        /// <param name="gamePath">Directory path to the game installation.</param>
        /// <param name="storePath">Path to the file used for storing definitions and history.</param>
        /// <param name="language">Initial language to use.</param>
        /// <param name="libraPath">Path to the Libra Eorzea database file.</param>
        public ARealmReversed(string gamePath, string storePath, Language language, string libraPath) : this(new DirectoryInfo(gamePath), new FileInfo(storePath), language, new FileInfo(libraPath)) { }

        /// <summary>
        ///     Initializes a new instance of the <see cref="ARealmReversed" /> class.
        /// </summary>
        /// <param name="gameDirectory">Directory of the game installation.</param>
        /// <param name="storeFile">File used for storing definitions and history.</param>
        /// <param name="language">Initial language to use.</param>
        /// <param name="libraFile">Location of the Libra Eorzea database file, or <c>null</c> if it should not be used.</param>
        public ARealmReversed(DirectoryInfo gameDirectory, FileInfo storeFile, Language language, FileInfo libraFile) {
            _GameDirectory = gameDirectory;
            _Packs = new PackCollection(Path.Combine(gameDirectory.FullName, "game", "sqpack"));
            _GameData = new XivCollection(Packs, libraFile) {
                ActiveLanguage = language
            };

            _GameVersion = File.ReadAllText(Path.Combine(gameDirectory.FullName, "game", "ffxivgame.ver"));
            _StateFile = storeFile;

            using (var zipFile = new ZipFile(StateFile.FullName, ZipEncoding)) {
                if (zipFile.ContainsEntry(VersionFile)) {
                    RelationDefinition fsDef = null, zipDef = null;
                    DateTime fsMod = DateTime.MinValue, zipMod = DateTime.MinValue;
                    if (!TryGetDefinitionVersion(zipFile, GameVersion, out zipDef, out zipMod))
                        zipDef = ReadDefinition(zipFile, DefinitionFile,  out zipMod);
                    if (!TryGetDefinitionFromFileSystem(out fsDef, out fsMod))
                        fsDef = null;

                    if (fsDef != null && fsMod > zipMod) {
                        fsDef.Version = GameVersion;
                        _GameData.Definition = fsDef;
                        StoreDefinition(zipFile, fsDef, DefinitionFile);
                        zipFile.Save();
                    } else
                        _GameData.Definition = zipDef;
                } else
                    _GameData.Definition = Setup(zipFile);
            }

            _GameData.Definition.Compile();
        }

        #endregion

        #region Shared
        private bool TryGetDefinitionFromFileSystem(out RelationDefinition definition, out DateTime lastWrite) {
            var file = new FileInfo(Path.Combine(StateFile.Directory.FullName, DefinitionFile));
            return TryGetDefinitionFromFileSystem(file, out definition, out lastWrite);
        }
        private bool TryGetDefinitionFromFileSystem(FileInfo file, out RelationDefinition definition, out DateTime lastWrite) {
            if (file.Exists) {
                lastWrite = file.LastWriteTimeUtc;
                definition = RelationDefinition.Deserialize(file.FullName);
                return true;
            }

            lastWrite = DateTime.MinValue;
            definition = null;
            return false;
        }

        /// <summary>
        ///     Store the current pack files in storage.
        /// </summary>
        /// <param name="zip"><see cref="ZipFile" /> to store the current packs in.</param>
        private void StorePacks(ZipFile zip) {
            const string ExdPackPattern = "0a*.*";

            foreach (var file in Packs.DataDirectory.EnumerateFiles(ExdPackPattern, SearchOption.AllDirectories)) {
                string targetDir = GameVersion + "/" + file.Directory.Name;
                zip.UpdateFile(file.FullName, targetDir);
            }
        }

        /// <summary>
        ///     Updating the current version string in storage.
        /// </summary>
        /// <param name="zip"><see cref="ZipFile" /> to store the version string in.</param>
        private void UpdateVersion(ZipFile zip) {
            zip.UpdateEntry(VersionFile, GameVersion);
        }

        /// <summary>
        ///     Copy a file entry inside a <see cref="ZipFile" />.
        /// </summary>
        /// <param name="zip"><see cref="ZipFile" /> on which to perform the operation.</param>
        /// <param name="source">Source file name to copy.</param>
        /// <param name="target">Destination file name for the copy.</param>
        private static void ZipCopy(ZipFile zip, string source, string target) {
            var entry = zip[source];

            byte[] buffer;
            using (var s = entry.OpenReader()) {
                using (var ms = new MemoryStream()) {
                    s.CopyTo(ms);
                    buffer = ms.ToArray();
                }
            }

            zip.UpdateEntry(target, buffer);
        }

        /// <summary>
        ///     Deserialize a <see cref="RelationDefinition" /> file inside a <see cref="ZipFile" />.
        /// </summary>
        /// <param name="zip"><see cref="ZipFile" /> to read from.</param>
        /// <param name="entry">File name of the definition to read.</param>
        /// <returns>Returns the read <see cref="RelationDefinition" />.</returns>
        private static RelationDefinition ReadDefinition(ZipFile zip, string entry = DefinitionFile) {
            DateTime mod;
            return ReadDefinition(zip, entry, out mod);
        }

        private static RelationDefinition ReadDefinition(ZipFile zip, string entry, out DateTime lastModified) {
            RelationDefinition def;

            var zipEntry = zip[entry];
            lastModified = zipEntry.LastModified.ToUniversalTime();
            using (var s = zipEntry.OpenReader()) {
                using (var r = new StreamReader(s, ZipEncoding))
                    def = RelationDefinition.Deserialize(r);
            }

            return def;
        }

        /// <summary>
        ///     Serialize a <see cref="RelationDefinition" /> into a <see cref="ZipFile" />.
        /// </summary>
        /// <param name="zip"><see cref="ZipFile" /> to write to.</param>
        /// <param name="definition"><see cref="RelationDefinition" /> to store.</param>
        /// <param name="path">File name inside the storage to write to.</param>
        private static void StoreDefinition(ZipFile zip, RelationDefinition definition, string path) {
            using (var ms = new MemoryStream()) {
                using (var writer = new StreamWriter(ms, ZipEncoding)) {
                    definition.Serialize(writer);
                    writer.Flush();
                    zip.UpdateEntry(path, ms.ToArray());
                }
            }
        }

        /// <summary>
        ///     Store a <see cref="UpdateReport" /> in a <see cref="ZipFile" />.
        /// </summary>
        /// <param name="zip"><see cref="ZipFile" /> to write to.</param>
        /// <param name="report"><see cref="UpdateReport" /> to store.</param>
        private static void StoreReport(ZipFile zip, UpdateReport report) {
            var textTarget = string.Format(UpdateReportTextFile, report.PreviousVersion, report.UpdateVersion);
            zip.UpdateEntry(textTarget, string.Join(Environment.NewLine, report.Changes.Select(_ => _.ToString())),
                ZipEncoding);

            var yamlTarget = string.Format(UpdateReportYamlFile, report.PreviousVersion, report.UpdateVersion);
            var serializer = new Serializer();
            byte[] yamlBuffer;
            using (var ms = new MemoryStream()) {
                using (TextWriter writer = new StreamWriter(ms, ZipEncoding)) {
                    serializer.Serialize(writer, report);
                    writer.Flush();
                    yamlBuffer = ms.ToArray();
                }
            }
            zip.UpdateEntry(yamlTarget, yamlBuffer);

            var binTarget = string.Format(UpdateReportBinFile, report.PreviousVersion, report.UpdateVersion);
            var formatter = new BinaryFormatter();
            byte[] binBuffer;

            using (var ms = new MemoryStream()) {
                formatter.Serialize(ms, report);
                binBuffer = ms.ToArray();
            }

            zip.UpdateEntry(binTarget, binBuffer);
        }

        #endregion

        #region Update

        /// <summary>
        ///     Attempt to get the <see cref="RelationDefinition" /> for a specific version from storage.
        /// </summary>
        /// <param name="zip"><see cref="ZipFile" /> to read from.</param>
        /// <param name="version">Definition version to look for.</param>
        /// <param name="definition">
        ///     When this method returns, contains the <see cref="RelationDefinition" /> for the specified
        ///     version, if found; otherwise, <c>null</c>.
        /// </param>
        /// <returns><c>true</c> if the definition for the specified version was present; <c>false</c> otherwise.</returns>
        private bool TryGetDefinitionVersion(ZipFile zip, string version, out RelationDefinition definition) {
            DateTime mod;
            return TryGetDefinitionVersion(zip, version, out definition, out mod);
        }
        private bool TryGetDefinitionVersion(ZipFile zip, string version, out RelationDefinition definition, out DateTime lastMod) {
            var storedVersionEntry = zip[VersionFile];
            string storedVersion;
            using (var s = storedVersionEntry.OpenReader()) {
                using (var r = new StreamReader(s))
                    storedVersion = r.ReadToEnd();
            }

            if (storedVersion != version) {
                var existingDefPath = string.Format("{0}/{1}", version, DefinitionFile);
                if (zip.ContainsEntry(existingDefPath)) {
                    ZipCopy(zip, existingDefPath, DefinitionFile);
                    UpdateVersion(zip);
                    zip.Save();

                    definition = ReadDefinition(zip, DefinitionFile, out lastMod);
                    return true;
                }

                definition = null;
                lastMod = DateTime.MinValue;
                return false;
            }

            definition = ReadDefinition(zip, DefinitionFile, out lastMod);
            return true;
        }

        /// <summary>
        ///     Update to the current version.
        /// </summary>
        /// <param name="detectDataChanges">Boolean indicating whether the update should also look for changes in data.</param>
        /// <param name="progress">Optional object to which update progress is reported.</param>
        /// <returns>Returns the <see cref="UpdateReport" /> containing all changes.</returns>
        /// <exception cref="InvalidOperationException">Definition is up-to-date.</exception>
        public UpdateReport Update(bool detectDataChanges, IProgress<UpdateProgress> progress = null) {
            if (DefinitionVersion == GameVersion)
                throw new InvalidOperationException();

            var previousVersion = DefinitionVersion;

            var exdPackId = new PackIdentifier("exd", PackIdentifier.DefaultExpansion, 0);
            var exdPack = Packs.GetPack(exdPackId);
            var exdOldKeepInMemory = exdPack.KeepInMemory;
            exdPack.KeepInMemory = true;

            string tempPath = null;
            UpdateReport report;
            try {
                using (var zip = new ZipFile(StateFile.FullName, ZipEncoding)) {
                    tempPath = ExtractPacks(zip, previousVersion);
                    var previousPack = new PackCollection(Path.Combine(tempPath, previousVersion));
                    previousPack.GetPack(exdPackId).KeepInMemory = true;
                    var previousDefinition = ReadDefinition(zip);

                    var updater = new RelationUpdater(previousPack, previousDefinition, Packs, GameVersion, progress);

                    var changes = updater.Update(detectDataChanges);
                    report = new UpdateReport(previousVersion, GameVersion, changes);

                    var definition = updater.Updated;

                    StorePacks(zip);
                    StoreDefinition(zip, definition, DefinitionFile);
                    StoreDefinition(zip, definition, string.Format("{0}/{1}", definition.Version, DefinitionFile));
                    StoreReport(zip, report);
                    zip.Save();

                    GameData.Definition = definition;
                    GameData.Definition.Compile();
                }
            } finally {
                if (exdPack != null)
                    exdPack.KeepInMemory = exdOldKeepInMemory;
                if (tempPath != null) {
                    try {
                        Directory.Delete(tempPath, true);
                    } catch {
                        Console.Error.WriteLine("Failed to delete temporary directory {0}.", tempPath);
                    }
                }
            }
            return report;
        }

        /// <summary>
        ///     Extract the packs of a specific version from storage into a temporary directory.
        /// </summary>
        /// <param name="zip"><see cref="ZipFile" /> to read from.</param>
        /// <param name="previousVersion">Version of the packs to extract.</param>
        /// <returns>Returns the path to the directory containing the extracted packs.</returns>
        private static string ExtractPacks(ZipFile zip, string previousVersion) {
            var tempPath = Path.GetTempFileName();
            File.Delete(tempPath);
            Directory.CreateDirectory(tempPath);

            foreach (var entry in zip.Entries.Where(e => e.FileName.StartsWith(previousVersion)))
                    entry.Extract(tempPath);

            return tempPath;
        }

        #endregion
    }
}
