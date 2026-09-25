using System.IO;

namespace ProgramDock.Services;

public static class ExecutableInspector
{
    public static bool IsConsoleExecutable(string path)
    {
        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new BinaryReader(stream);
            if (stream.Length < 0x40 || reader.ReadUInt16() != 0x5A4D)
            {
                return false;
            }

            stream.Position = 0x3C;
            var peOffset = reader.ReadInt32();
            if (peOffset <= 0 || peOffset > stream.Length - 4)
            {
                return false;
            }

            stream.Position = peOffset;
            if (reader.ReadUInt32() != 0x00004550)
            {
                return false;
            }

            var optionalHeaderOffset = stream.Position + 20;
            stream.Position = optionalHeaderOffset;
            var magic = reader.ReadUInt16();
            if (magic is not 0x10B and not 0x20B)
            {
                return false;
            }

            var subsystemOffset = optionalHeaderOffset + 68;
            if (subsystemOffset < 0 || subsystemOffset > stream.Length - 2)
            {
                return false;
            }

            stream.Position = subsystemOffset;
            return reader.ReadUInt16() == 3; // IMAGE_SUBSYSTEM_WINDOWS_CUI
        }
        catch
        {
            return false;
        }
    }
}
