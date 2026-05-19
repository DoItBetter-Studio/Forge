using System;

namespace Glyphborn.Forge.Maths
{
	public struct Vec3
	{
		public float x;
		public float y;
		public float z;

		public Vec3(float x, float y, float z)
		{
			this.x = x;
			this.y = y;
			this.z = z;
		}

		public static Vec3 Add(Vec3 a, Vec3 b)
			=> new(a.x + b.x, a.y + b.y, a.z + b.z);

		public static Vec3 Sub(Vec3 a, Vec3 b)
			=> new(a.x - b.x, a.y - b.y, a.z - b.z);

		public static Vec3 Scale(Vec3 v, float s)
			=> new(v.x * s, v.y * s, v.z * s);

		public static float Dot(Vec3 a, Vec3 b)
			=> a.x * b.x + a.y * b.y + a.z * b.z;

		public static Vec3 Cross(Vec3 a, Vec3 b)
			=> new(
				a.y * b.z - a.z * b.y,
				a.z * b.x - a.x * b.z,
				a.x * b.y - a.y * b.x
			);

		public static float Length(Vec3 v)
			=> MathF.Sqrt(v.x * v.x + v.y * v.y + v.z * v.z);

		public static Vec3 Normalize(Vec3 v)
		{
			float len = Length(v);
			if (len == 0.0f)
				return new Vec3(0, 0, 0);
			return new Vec3(v.x / len, v.y / len, v.z / len);
		}
	}
}
