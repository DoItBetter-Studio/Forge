using System;

namespace Glyphborn.Forge.Maths
{
	public struct Vec2
	{
		public float x;
		public float y;

		public static Vec2 Zero = new Vec2(0, 0);

		public Vec2(float x, float y) 
		{
			this.x = x;
			this.y = y;
		}

		public static Vec2 Add(Vec2 v1, Vec2 v2) => new Vec2(v1.x + v2.x, v1.y + v2.y);

		public static Vec2 Sub(Vec2 v1, Vec2 v2) => new Vec2(v1.x - v2.x, v1.y - v2.y);

		public static Vec2 Scale(Vec2 v, float s) => new Vec2(v.x * s, v.y * s);

		public static float Dot(Vec2 v1, Vec2 v2) => v1.x * v2.x + v1.y * v2.y;

		public static float Length(Vec2 v) => MathF.Sqrt(v.x * v.x + v.y * v.y);

		public static Vec2 Normalize(Vec2 v)
		{
			float len = Length(v);
			if (len == 0.0f)
				return new Vec2(0, 0);
			return new Vec2(v.x / len, v.y / len);
		}
	}
}
