using System.IO;

namespace Glyphborn.Forge.Import
{
	public static class ModelImporter
	{
		public static ImportResult Import(string path)
		{
			string ext = Path.GetExtension(path).ToLowerInvariant();

			switch (ext)
			{
				case ".gltf":
				case ".glb":
					return SharpGltfImporter.Import(path);

				default:
					return AssimpImporter.Import(path);
			}
		}
	}
}
