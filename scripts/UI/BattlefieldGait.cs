using Godot;

/// <summary>How the men on the field walk, stand, draw and fall: the gait worked out for a figure
/// moved by soldier.gdshader, and the clip played for a figure baked from its clips.</summary>
public partial class BattlefieldSquads
{
	/// <summary>A man's gait this frame, for soldier.gdshader: how much he is walking, where he is in
	/// his stride, how long it is, and how far he leans into a blow. His stride goes on exactly as far
	/// as his feet have carried him, so they do not slide over the ground whatever his pace.</summary>
	private Color Stride(object walker, SoldierFigure figure, Vector2 now, float delta, float lean)
	{
		if (!_gaits.TryGetValue(walker, out Gait gait))
		{
			gait = new Gait { Phase = Mathf.Abs(walker.GetHashCode()) % 628 / 100f, Last = now };
			_gaits[walker] = gait;
		}

		float pace = delta <= 0f ? 0f : gait.Last.DistanceTo(now) / delta;
		gait.Last = now;
		gait.March = Mathf.Lerp(gait.March, Mathf.Clamp(pace / FullStride, 0f, 1f), Mathf.Min(1f, delta * SettingOff));
		float swing = Mathf.Min(StrideAtWalk + (StridePerPace * pace), StrideMost);
		float perStride = 4f * figure.LegShare * figure.Stature * Mathf.Sin(swing);
		gait.Phase = (gait.Phase + (Mathf.Tau * pace / perStride * delta)) % Mathf.Tau;
		return new Color(gait.March, gait.Phase, swing, lean);
	}

	/// <summary>Which frame of his clips a baked man is at: walking when his feet are carrying him —
	/// the clip run exactly as fast as he covers ground, so his feet do not slide — drawing and
	/// loosing when his squad shoots, and standing easy otherwise.
	///
	/// The shot is timed from the battle's own arrow: the clip is run so that the frame where his
	/// fingers leave the string is the moment the battle looses (man.Struck), drawing up to it while
	/// he is making ready and following through after.</summary>
	private Color Played(FieldSoldier man, SoldierFigure figure, Vector2 now, float delta, float clock, float between)
	{
		if (!_gaits.TryGetValue(man, out Gait gait))
		{
			gait = new Gait { Phase = Mathf.Abs(man.GetHashCode()) % 100 / 10f, Last = now, Clip = IdleClip };
			_gaits[man] = gait;
		}

		float pace = delta <= 0f ? 0f : gait.Last.DistanceTo(now) / delta;
		gait.Last = now;

		// Walking starts at a stride and stops at a shuffle: with one threshold, a man easing into
		// his place a few centimetres at a time flickered between walking and standing every frame.
		gait.IsWalking = pace > (gait.IsWalking ? StopsWalking : StartsWalking);
		(string clip, float time) = Chosen(man, figure, gait, pace, delta, clock, between);

		// From one clip into another over a quarter of a second, the old one playing on beneath the
		// new as it fades: cut from one to the other, a man jerked from pose to pose.
		if (clip != gait.Clip)
		{
			(gait.Was, gait.WasTime, gait.Fade) = (gait.Clip, gait.Time, 0f);
			gait.Clip = clip;
		}

		gait.Time = time;
		gait.WasTime += delta;
		gait.Fade = Mathf.Min(1f, gait.Fade + (delta / CrossFade));
		float old = gait.Was == null ? 0f : 1f - gait.Fade;
		return new Color(figure.Row(clip, time), figure.Row(gait.Was ?? clip, gait.WasTime), old, 0f);
	}

	/// <summary>The clip a baked man should be playing, and how far into it: walking when his feet
	/// are carrying him, drawing and loosing when his squad shoots, standing easy otherwise.</summary>
	private static (string Clip, float Time) Chosen(FieldSoldier man, SoldierFigure figure, Gait gait, float pace,
		float delta, float clock, float between)
	{
		if (gait.IsWalking)
		{
			gait.Phase += delta * pace / (ClipWalkPace * figure.Stature / figure.Bounds.Size.Y);
			return (WalkClip, gait.Phase);
		}

		float shot = figure.Clips[ShootClip].Seconds;
		float sinceLoose = clock - man.Struck;
		if (man.Squad.Shoots && man.Foe == null)
		{
			if (sinceLoose < shot - LooseInShoot)
			{
				return (ShootClip, LooseInShoot + sinceLoose);
			}

			// How long until he looses, now, between the battle's slices — read only at them, the
			// draw went up in jerks ten times a second.
			// Between arrows he stays in the shooting stance, at the start of the draw, and does not
			// drop back to standing easy: the two clips plant the feet differently, and going from one
			// to the other and back with every arrow, his feet shuffled twice a shot.
			float ready = man.Ready - (between * FieldBattle.Slice);
			if (man.Squad.ShootingAt != null)
			{
				return (ShootClip, Mathf.Max(0f, LooseInShoot - ready));
			}
		}

		return (IdleClip, clock + gait.Phase);
	}
}
