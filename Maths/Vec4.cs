namespace Glyphborn.Forge.Maths
{
	public struct Vec4
	{
		public float x;
		public float y;
		public float z;
		public float w;

		public Vec4(float x, float y, float z, float w)
		{
			this.x = x;
			this.y = y;
			this.z = z;
			this.w = w;
		}

		public static Vec4 FromVec3(float x, float y, float z)
			=> new(x, y, z, 1.0f);

		public static Vec4 FromVec3(Vec3 vec)
			=> new(vec.x, vec.y, vec.z, 1.0f);

		public static Vec4 Add(Vec4 a, Vec4 b)
			=> new(a.x + b.x, a.y + b.y, a.z + b.z, a.w + b.w);

		public static Vec4 Sub(Vec4 a, Vec4 b)
			=> new(a.x - b.x, a.y - b.y, a.z - b.z, a.w - b.w);

		public static Vec4 Scale(Vec4 v, float s)
			=> new(v.x * s, v.y * s, v.z * s, v.w * s);
	}
}
