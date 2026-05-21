using Glyphborn.Forge.Data;

namespace Glyphborn.Forge.Import
{
	public class ImportResult
	{
		public MeshDocument? Mesh { get; set; }
		public SkeletonDocument? Skeleton { get; set; }
		public AnimationDocument? Animation { get; set; }

		public bool HasMesh => Mesh != null;
		public bool HasSkeleton => Skeleton != null;
		public bool HasAnimation => Animation != null;
	}
}
