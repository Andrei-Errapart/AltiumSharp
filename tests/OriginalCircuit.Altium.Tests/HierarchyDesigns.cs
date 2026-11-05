using OriginalCircuit.Altium.Models;
using OriginalCircuit.Altium.Models.Project;
using OriginalCircuit.Altium.Models.Sch;
using OriginalCircuit.Altium.Serialization.Writers;
using OriginalCircuit.Eda.Primitives;

namespace OriginalCircuit.Altium.Tests;

/// <summary>Builders for synthetic designs used by the hierarchy loading tests.</summary>
internal static class HierarchyDesigns
{
    public static SchDocument Sheet(string fileName, params object[] content)
    {
        var doc = new SchDocument { FileName = fileName };
        foreach (var item in content)
        {
            switch (item)
            {
                case SchComponent component: doc.AddComponent(component); break;
                default: doc.AddPrimitive(item); break;
            }
        }
        return doc;
    }

    public static SchComponent Part(string designator, string uid)
    {
        var component = new SchComponent { Name = "PART", UniqueId = uid, CurrentPartId = 1, PartCount = 1 };
        component.AddParameter(new SchParameter { Name = "Designator", Value = designator });
        return component;
    }

    public static SchSheetSymbol Symbol(string designator, string fileName, string uid, int xMils = 1000, int yMils = 1000) => new()
    {
        Location = new CoordPoint(Coord.FromMils(xMils), Coord.FromMils(yMils)),
        XSize = Coord.FromMils(500),
        YSize = Coord.FromMils(400),
        FileName = fileName,
        SheetName = designator,
        NameLabel = new SchLabel { Text = designator },
        UniqueId = uid,
    };

    public static AltiumProject Project(params string[] documents)
    {
        var project = AltiumLibrary.CreateProject();
        foreach (var document in documents)
            project.AddDocument(document);
        return project;
    }

    public static byte[] Bytes(SchDocument document)
    {
        using var ms = new MemoryStream();
        new SchDocWriter().Write(document, ms);
        return ms.ToArray();
    }

    public static string[] Designators(ProjectHierarchy hierarchy) =>
        hierarchy.Components.Select(c => c.Designator).OrderBy(d => d, StringComparer.Ordinal).ToArray();

    // Files keyed by their path as Windows opens it (case-insensitive).
    public static Dictionary<string, byte[]> Files(params (string Path, SchDocument Sheet)[] files)
    {
        var result = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, sheet) in files)
            result[ProjectHierarchy.NormalizePath(path)!] = Bytes(sheet);
        return result;
    }

    // A temporary project folder written from the files, deleted on dispose.
    public sealed class Folder : IDisposable
    {
        public Folder(params (string Path, SchDocument Sheet)[] files)
        {
            Root = Path.Combine(Path.GetTempPath(), "Hierarchy_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            foreach (var (path, sheet) in files)
                Write(path, Bytes(sheet));
        }

        public string Root { get; }

        public void Write(string relativePath, byte[] bytes)
        {
            var full = Path.Combine(Root, relativePath.Replace('\\', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllBytes(full, bytes);
        }

        public AltiumProject Project(params string[] documents)
        {
            var project = HierarchyDesigns.Project(documents);
            project.FilePath = Path.Combine(Root, "ACME-CTRL01.PrjPcb");
            return project;
        }

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); }
            catch (IOException) { }
        }
    }
}
