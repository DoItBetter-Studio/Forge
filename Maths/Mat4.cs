using System;

namespace Glyphborn.Forge.Maths
{
	public struct Mat4
	{
		// m[column, row]
		public float[,] m;

		public static Mat4 Identity()
		{
			var r = new Mat4 { m = new float[4, 4] };
			r.m[0, 0] = 1f;
			r.m[1, 1] = 1f;
			r.m[2, 2] = 1f;
			r.m[3, 3] = 1f;
			return r;
		}

		public static Mat4 Translate(Vec3 v)
		{
			var r = Identity();
			r.m[3, 0] = v.x;
			r.m[3, 1] = v.y;
			r.m[3, 2] = v.z;
			return r;
		}

		public static Mat4 Scale(Vec3 v)
		{
			var r = Identity();
			r.m[0, 0] = v.x;
			r.m[1, 1] = v.y;
			r.m[2, 2] = v.z;
			return r;
		}

		public static Mat4 RotateX(float angle)
		{
			var r = Identity();
			float c = MathF.Cos(angle);
			float s = MathF.Sin(angle);
			r.m[1, 1] = c;
			r.m[1, 2] = s;
			r.m[2, 1] = -s;
			r.m[2, 2] = c;
			return r;
		}

		public static Mat4 RotateY(float angle)
		{
			var r = Identity();
			float c = MathF.Cos(angle);
			float s = MathF.Sin(angle);
			r.m[0, 0] = c;
			r.m[0, 2] = -s;
			r.m[2, 0] = s;
			r.m[2, 2] = c;
			return r;
		}

		public static Mat4 RotateZ(float angle)
		{
			var r = Identity();
			float c = MathF.Cos(angle);
			float s = MathF.Sin(angle);
			r.m[0, 0] = c;
			r.m[0, 1] = s;
			r.m[1, 0] = -s;
			r.m[1, 1] = c;
			return r;
		}

		public static Mat4 Multiply(Mat4 a, Mat4 b)
		{
			var r = new Mat4 { m = new float[4, 4] };

			for (int i = 0; i < 4; i++)
			{
				for (int j = 0; j < 4; j++)
				{
					r.m[i, j] =
						a.m[0, j] * b.m[i, 0] +
						a.m[1, j] * b.m[i, 1] +
						a.m[2, j] * b.m[i, 2] +
						a.m[3, j] * b.m[i, 3];
				}
			}

			return r;
		}

		public static Vec4 MulVec4(Mat4 m, Vec4 v)
		{
			return new Vec4(
				m.m[0, 0] * v.x + m.m[1, 0] * v.y + m.m[2, 0] * v.z + m.m[3, 0] * v.w,
				m.m[0, 1] * v.x + m.m[1, 1] * v.y + m.m[2, 1] * v.z + m.m[3, 1] * v.w,
				m.m[0, 2] * v.x + m.m[1, 2] * v.y + m.m[2, 2] * v.z + m.m[3, 2] * v.w,
				m.m[0, 3] * v.x + m.m[1, 3] * v.y + m.m[2, 3] * v.z + m.m[3, 3] * v.w
			);
		}

		public static Mat4 Perspective(float fov, float aspect, float near, float far)
		{
			var r = new Mat4 { m = new float[4, 4] };
			float f = 1.0f / MathF.Tan(fov * 0.5f);

			r.m[0, 0] = f / aspect;
			r.m[1, 1] = f;
			r.m[2, 2] = (far + near) / (near - far);
			r.m[2, 3] = -1.0f;
			r.m[3, 2] = (2.0f * far * near) / (near - far);

			return r;
		}

		public static Mat4 LookAt(Vec3 eye, Vec3 target, Vec3 worldUp)
		{
			Vec3 forward = Vec3.Normalize(Vec3.Sub(target, eye));

			// Lock up vector to world Y
			Vec3 right = Vec3.Normalize(Vec3.Cross(forward, worldUp));
			Vec3 up = Vec3.Cross(right, forward);

			var m = Mat4.Identity();

			// Column-major
			m.m[0, 0] = right.x;
			m.m[0, 1] = right.y;
			m.m[0, 2] = right.z;

			m.m[1, 0] = up.x;
			m.m[1, 1] = up.y;
			m.m[1, 2] = up.z;

			m.m[2, 0] = -forward.x;
			m.m[2, 1] = -forward.y;
			m.m[2, 2] = -forward.z;

			m.m[3, 0] = -Vec3.Dot(right, eye);
			m.m[3, 1] = -Vec3.Dot(up, eye);
			m.m[3, 2] = Vec3.Dot(forward, eye);
			m.m[3, 3] = 1.0f;

			return m;
		}
	}
}
