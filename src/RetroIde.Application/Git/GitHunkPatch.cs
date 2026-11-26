using System.Security.Cryptography;
using System.Text;

namespace RetroIde.Application.Git;

/// <summary>Identifies the selectable portions of a native, single-file Git text diff.</summary>
internal sealed class GitHunkPatch
{
    internal sealed record Hunk(int Id, int DiffLine, string Header);

    private readonly byte[] _patch;
    private readonly (int Offset, Hunk Info)[] _hunks;

    private GitHunkPatch(byte[] patch, (int Offset, Hunk Info)[] hunks, GitWorktree repository, string path, string layer)
    {
        _patch = patch;
        _hunks = hunks;
        Hunks = [.. hunks.Select(hunk => hunk.Info)];
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes($"{repository.WorktreePath}\0{repository.GitDirectory}\0{path}\0{layer}\0"));
        hash.AppendData(patch);
        Preimage = Convert.ToHexString(hash.GetHashAndReset());
    }

    internal string Preimage { get; }
    internal Hunk[] Hunks { get; }

    internal static GitHunkPatch? Parse(byte[] patch, GitWorktree repository, string path, string layer)
    {
        if (!patch.AsSpan().StartsWith("diff --git "u8))
            return null;

        var hunks = new List<(int Offset, Hunk Info)>();
        var ordinaryMode = false;
        var offset = 0;
        var lineNumber = 1;
        while (offset < patch.Length)
        {
            var remainder = patch.AsSpan(offset);
            var newline = remainder.IndexOf((byte)'\n');
            var line = newline < 0 ? remainder : remainder[..newline];
            if (offset > 0 && line.StartsWith("diff --git "u8))
                return null;
            if (hunks.Count == 0 && line.StartsWith("index "u8))
                ordinaryMode = line.EndsWith(" 100644"u8) || line.EndsWith(" 100755"u8);
            if (line.StartsWith("@@ "u8))
            {
                if (!ordinaryMode)
                    return null;
                hunks.Add((offset, new Hunk(hunks.Count + 1, lineNumber, Encoding.UTF8.GetString(line))));
            }
            if (newline < 0)
                break;
            offset += newline + 1;
            lineNumber++;
        }

        return hunks.Count == 0 ? null : new(patch, [.. hunks], repository, path, layer);
    }

    internal byte[] Select(int[] ids)
    {
        using var patch = new MemoryStream();
        patch.Write(_patch, 0, _hunks[0].Offset);
        foreach (var id in ids.Order())
        {
            var start = _hunks[id - 1].Offset;
            var end = id == _hunks.Length ? _patch.Length : _hunks[id].Offset;
            patch.Write(_patch, start, end - start);
        }
        return patch.ToArray();
    }
}
