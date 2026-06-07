// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using System.Threading.Tasks;
using osu.Framework.Bindables;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Screens;
using osu.Game.Configuration;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.UI;
using osu.Game.Screens.Backgrounds;
using osu.Game.Screens.Play;
using osu.Game.Storyboards;
using osu.Game.Storyboards.Drawables;
using osuTK;

namespace osu.Game.Rulesets.Mods
{
    // Summary:
    // What this mod does is to grab the existing osu! game experience and transform it directly from third person to first person, using the playfield as is.
    //
    // This mod makes the playfield and storyboard be viewable in first person with background optionally being viewable in first person. A playfield centre aim reference is also available so
    // absolute position muscle memory can transfer over.
    public abstract partial class ModFirstPerson : Mod
    {
        public override string Name => "First Person";
        public override string Acronym => "FP";
        public override ModType Type => ModType.Fun; // TODO: Mod icon (on next line), and IncompatibleMods base.IncompatibleMods.Concat vs fresh new[]
        public override Type[] IncompatibleMods => new[] { typeof(ModCinema), typeof(ModRelax) };

        protected readonly Vector2 PlayfieldMiscPosScale = new Vector2(1.6f); // Brute-forced, is magical, todo: may need more intricate calculation

        protected abstract void InitialisePlayfieldForFirstPerson(Playfield playfield);

        protected abstract void FirstPersonify(BackgroundScreenBeatmap backgroundScreenBeatmap);
        protected abstract void FirstPersonify(DrawableStoryboard drawableStoryboard);
        protected abstract void FirstPersonify(Container container);

        protected abstract PlayfieldAdjustmentContainer CreatePlayfieldAdjustmentContainer(DrawableRuleset drawableRuleset);

        protected abstract void DefaultBackground(BackgroundScreenBeatmap backgroundScreenBeatmap);

        protected const float CENTRE_AIM_BOX_SHORTER_SIDE_LENGTH = 16.0f;
        protected const float CENTRE_AIM_BOX_LONGER_SIDE_LENGTH = 32.0f;
        protected const float CENTRE_AIM_ARM_DISTANCE = (CENTRE_AIM_BOX_LONGER_SIDE_LENGTH + CENTRE_AIM_BOX_SHORTER_SIDE_LENGTH) / 2.0f;

        [SettingSource("Centre aim reference", "Know where the centre of the playfield is.")]
        public BindableBool CentreAimReference { get; } = new BindableBool(true);

        [SettingSource("Centred background", "Have the background follow.")] // From the perspective of the player w.r.t. cursor/catcher/etc. Less ambiguous than e.g. "(un)adjusted"
        public BindableBool CentredBackground { get; } = new BindableBool();

        protected readonly Bindable<bool> ShowStoryboard = new Bindable<bool>();

        protected DrawableRuleset Ruleset = null!;
        protected Playfield Playfield = null!;
        protected PlayfieldAdjustmentContainer? PlayfieldCentreAimReference;
    }

    public abstract partial class ModFirstPerson<TObject> : ModFirstPerson, IReadFromConfig, IApplicableToDrawableRuleset<TObject>, IApplicableToPlayer, IApplicableToHUD
        where TObject : HitObject
    {
        public void ReadFromConfig(OsuConfigManager config)
        {
            config.BindWith(OsuSetting.ShowStoryboard, ShowStoryboard);
        }

        public virtual void ApplyToDrawableRuleset(DrawableRuleset<TObject> drawableRuleset)
        {
            Ruleset = drawableRuleset;

            Playfield = Ruleset.Playfield;
        }

        private bool confirmExited(Player player) => !player.IsCurrentScreen(); // Inspired from TestScenePause's confirmExited

        private DrawableStoryboard? getDrawableStoryboard(Player player) => player.DimmableStoryboard.Children.FirstOrDefault(d => d is DrawableStoryboard) as DrawableStoryboard;

        public void ApplyToPlayer(Player player)
        {
            // Easily the most important part of this mod as the playfield's coordinates dictate where other visual elements will be
            InitialisePlayfieldForFirstPerson(Playfield);

            // Other mods that use overlays such as Flashlight and Bubbles must adapt to the First-Person perspective for compatibility
            Ruleset.Overlays.OnUpdate += _ => FirstPersonify(Ruleset.Overlays);

            Storyboard storyboard = player.GameplayState.Storyboard;

            if (!CentredBackground.Value)
            {
                Action<Drawable> backgroundAction = null!;
                backgroundAction = _ => player.ApplyToBackground(bsb =>
                {
                    if (confirmExited(player)) // Background screen beatmap persists upon exiting the play, so manual event removal and its repositioning to the center is necessary
                    {
                        bsb.OnUpdate -= backgroundAction;
                        DefaultBackground(bsb);

                        return;
                    }

                    bool storyboardReplacesBackground = storyboard.ReplacesBackground && storyboard.HasDrawable; // Based on Player's
                    if (!storyboardReplacesBackground || !ShowStoryboard.Value)
                        FirstPersonify(bsb);
                });

                player.ApplyToBackground(bsb => bsb.OnUpdate += backgroundAction);
            }

            if (storyboard.HasDrawable)
            {
                DrawableStoryboard? drawableStoryboard = getDrawableStoryboard(player); // The drawable storyboard may still be loaded even if Show storyboard was just disabled while entering the play

                if (drawableStoryboard.IsNotNull())
                {
                    drawableStoryboard.OnUpdate += _ => FirstPersonify(drawableStoryboard);

                    return;
                }

                ShowStoryboard.BindValueChanged(ss => Task.Run(async () => // Task.Run to not have 'async' lambda with delegate returning 'void'
                {
                    if (!ss.NewValue)
                        return;

                    ShowStoryboard.UnbindEvents(); // Show storyboard being enabled even briefly during a play means the drawable storyboard will load into memory if the play continues long enough

                    drawableStoryboard = getDrawableStoryboard(player);

                    while (drawableStoryboard.IsNull())
                    {
                        await Task.Delay(200).ConfigureAwait(false);
                        if (confirmExited(player))
                            return;

                        drawableStoryboard = getDrawableStoryboard(player);
                    }

                    drawableStoryboard.OnUpdate += _ => FirstPersonify(drawableStoryboard);
                }), true);
            }
        }

        // Adds an unblockable-from-overlays, non-hide target, centred aim reference because normally, the center of the playfield is known, so First Person should be the same
        public virtual void ApplyToHUD(HUDOverlay overlay)
        {
            if (!CentreAimReference.Value)
                return;

            PlayfieldCentreAimReference = CreatePlayfieldAdjustmentContainer(Ruleset);

            PlayfieldCentreAimReference.Alpha = 0.75f;
            PlayfieldCentreAimReference.Colour = Colour4.AntiqueWhite;

            // Origin (0, 0)
            PlayfieldCentreAimReference.Add(new Box { Size = new Vector2(CENTRE_AIM_BOX_SHORTER_SIDE_LENGTH), Anchor = Anchor.Centre, Origin = Anchor.Centre });

            // Y-Axis
            PlayfieldCentreAimReference.Add(
                new Box { Y = -CENTRE_AIM_ARM_DISTANCE, Width = CENTRE_AIM_BOX_SHORTER_SIDE_LENGTH, Height = CENTRE_AIM_BOX_LONGER_SIDE_LENGTH, Anchor = Anchor.Centre, Origin = Anchor.Centre });
            PlayfieldCentreAimReference.Add(
                new Box { Y = CENTRE_AIM_ARM_DISTANCE, Width = CENTRE_AIM_BOX_SHORTER_SIDE_LENGTH, Height = CENTRE_AIM_BOX_LONGER_SIDE_LENGTH, Anchor = Anchor.Centre, Origin = Anchor.Centre });
        }
    }
}
