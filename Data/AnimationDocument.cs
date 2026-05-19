using System;
using System.Collections.Generic;
using System.Numerics;

namespace Glyphborn.Forge.Data
{
	public enum InterpolationMode
	{
		Step,
		Linear,
		CubicHermite  // future: full tangent support
	}

	/// <summary>
	/// A single keyframe value for one transform channel of one bone.
	/// </summary>
	public class Keyframe<T>
	{
		public int Frame { get; set; }
		public T Value { get; set; } = default!;
		public InterpolationMode Interpolation { get; set; } = InterpolationMode.Linear;

		public Keyframe() { }
		public Keyframe(int frame, T value, InterpolationMode interp = InterpolationMode.Linear)
		{
			Frame = frame;
			Value = value;
			Interpolation = interp;
		}
	}

	/// <summary>
	/// All keyframes for a single bone inside one animation clip.
	/// Each bone channel independently stores position, rotation, and scale tracks.
	/// </summary>
	public class BoneChannel
	{
		/// <summary>Index into the skeleton's bone list.</summary>
		public int BoneIndex { get; set; }

		public List<Keyframe<Vector3>> PositionKeys { get; set; } = new();
		public List<Keyframe<Quaternion>> RotationKeys { get; set; } = new();
		public List<Keyframe<Vector3>> ScaleKeys { get; set; } = new();

		public BoneChannel() { }
		public BoneChannel(int boneIndex) { BoneIndex = boneIndex; }

		// -----------------------------------------------------------------
		// Evaluation helpers
		// -----------------------------------------------------------------

		public Vector3 EvaluatePosition(float frame)
			=> EvaluateList(PositionKeys, frame, Vector3.Zero, LerpVec3);

		public Quaternion EvaluateRotation(float frame)
			=> EvaluateList(RotationKeys, frame, Quaternion.Identity, Quaternion.Slerp);

		public Vector3 EvaluateScale(float frame)
			=> EvaluateList(ScaleKeys, frame, Vector3.One, LerpVec3);

		private static T EvaluateList<T>(
			List<Keyframe<T>> keys,
			float frame,
			T defaultValue,
			Func<T, T, float, T> lerp)
		{
			if (keys.Count == 0) return defaultValue;
			if (frame <= keys[0].Frame) return keys[0].Value;
			if (frame >= keys[^1].Frame) return keys[^1].Value;

			for (int i = 0; i < keys.Count - 1; i++)
			{
				var a = keys[i];
				var b = keys[i + 1];

				if (frame >= a.Frame && frame <= b.Frame)
				{
					if (a.Interpolation == InterpolationMode.Step)
						return a.Value;

					float t = (frame - a.Frame) / (float)(b.Frame - a.Frame);
					return lerp(a.Value, b.Value, t);
				}
			}

			return defaultValue;
		}

		private static Vector3 LerpVec3(Vector3 a, Vector3 b, float t)
			=> Vector3.Lerp(a, b, t);
	}

	/// <summary>
	/// One named animation clip (e.g. "Idle", "WalkForward", "SwordSwing").
	/// </summary>
	public class AnimationClip
	{
		public string Name { get; set; } = "Untitled";

		/// <summary>Total frame count (exclusive — last valid frame is FrameCount - 1).</summary>
		public int FrameCount { get; set; } = 60;

		/// <summary>Playback speed in frames per second.</summary>
		public float FrameRate { get; set; } = 24f;

		public bool Loop { get; set; } = true;

		/// <summary>One channel per animated bone. Bones not listed here use bind pose.</summary>
		public List<BoneChannel> Channels { get; set; } = new();

		public AnimationClip() { }
		public AnimationClip(string name) { Name = name; }

		public BoneChannel GetOrCreateChannel(int boneIndex)
		{
			foreach (var ch in Channels)
				if (ch.BoneIndex == boneIndex)
					return ch;

			var newChannel = new BoneChannel(boneIndex);
			Channels.Add(newChannel);
			return newChannel;
		}

		/// <summary>
		/// Evaluate the full local transform for a bone at a given (fractional) frame.
		/// Returns bind pose values for bones without a channel.
		/// </summary>
		public (Vector3 position, Quaternion rotation, Vector3 scale) EvaluateBone(int boneIndex, float frame, SkeletonDocument skeleton)
		{
			foreach (var ch in Channels)
			{
				if (ch.BoneIndex == boneIndex)
				{
					return (
						ch.EvaluatePosition(frame),
						ch.EvaluateRotation(frame),
						ch.EvaluateScale(frame)
					);
				}
			}

			// Fall back to bind pose
			if (boneIndex >= 0 && boneIndex < skeleton.Bones.Count)
			{
				var bone = skeleton.Bones[boneIndex];
				return (bone.BindPosition, bone.BindRotation, bone.BindScale);
			}

			return (Vector3.Zero, Quaternion.Identity, Vector3.One);
		}
	}

	/// <summary>
	/// Full animation document (.gban export target).
	/// One document can hold multiple named clips (walk, run, attack, etc.).
	/// Each clip references bones by index — requires a companion skeleton.
	/// </summary>
	public class AnimationDocument
	{
		public string Name { get; set; } = "Untitled";

		/// <summary>Name of the skeleton this animation is authored for.</summary>
		public string SkeletonName { get; set; } = "";

		public List<AnimationClip> Clips { get; set; } = new();

		public AnimationDocument() { }
		public AnimationDocument(string name) { Name = name; }

		public AnimationClip? FindClip(string name)
		{
			foreach (var c in Clips)
				if (c.Name == name)
					return c;
			return null;
		}

		public AnimationClip AddClip(string name)
		{
			var clip = new AnimationClip(name);
			Clips.Add(clip);
			return clip;
		}
	}
}