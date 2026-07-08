using Kingmaker.Code.UI.MVVM.VM.Dialog; // DialogContextVM
using RTAccess.UI;                        // Navigation, UiContexts
using RTAccess.UI.Graph;                  // ControlId

namespace RTAccess.Screens
{
    /// <summary>
    /// Shared shell for the two conversation transcript readers — ordinary dialogue (<see cref="DialogueScreen"/>)
    /// and the illustrated book event (<see cref="BookEventScreen"/>). Both ride the SAME DialogContextVM
    /// (Surface OR Space) and read like a transcript: the passage/scrollback rows then the answers, at layer 15,
    /// with <see cref="Screen.KeepStateOnPop"/> so a cutscene/pause hide doesn't lose the place. Both track the
    /// current page/cue by a STABLE identity (<typeparamref name="TId"/> — a BlueprintCue / BlueprintBookPage,
    /// NOT the per-fire VM instance) and, on a new one, home focus to its top SILENTLY (announce:false) while
    /// speaking the freshly-delivered line exactly once, QUEUED (a transcript never interrupts speech).
    ///
    /// Subclasses supply the VM, the identity, the home node, and the spoken line. The dialogue subclass layers
    /// its fade-command shadowing / new-conversation reset (<see cref="PreUpdate"/>), deliverability gate
    /// (<see cref="Deliverable"/>), stable-BlueprintCue dedup (<see cref="SameId"/>), and number-select
    /// (<see cref="PostUpdate"/>) on top, and keeps its own <see cref="Screen.Build"/> (history + the Enter-through
    /// cue row + the DialogChoiceGate answers). The book event uses the defaults and a passage/answers Build.
    /// </summary>
    public abstract class TranscriptScreen<TVm, TId> : Screen
        where TVm : class
        where TId : class
    {
        public override int Layer => 15; // over the in-game context + service windows
        // A transcript "pops" without closing (it hides during cutscene gaps / under the pause menu) — keep
        // the nav state so focus survives the gap.
        public override bool KeepStateOnPop => true;

        // In-area OR star-system context — the DialogContextVM lives on whichever StaticPartVM is live
        // (Surface or Space), exactly as RootUIContext.HasDialog resolves it.
        protected static DialogContextVM Context() => UiContexts.Dialog();

        // The page/cue focus was last homed to, and the one we've read aloud. Kept by stable identity so a
        // fresh VM instance for the same line (HandleOnCueShow re-fire) isn't re-homed / double-read.
        protected TId _focused;
        protected TId _spoken;

        /// <summary>The live VM (a DialogVM / BookEventVM), or null when the transcript isn't up.</summary>
        protected abstract TVm Vm();

        public override bool IsActive() => Vm() != null;

        public override void OnPush() => Reset();

        // A hide (cutscene gap / pause menu) POPS us with KeepStateOnPop=true. Clear ONLY the focus marker so the
        // next OnUpdate re-homes focus to the CURRENT page/cue (WA ff35982 — otherwise re-showing lands on the
        // oldest transcript row); keep the spoken marker so the line isn't re-read on a mere hide. A real close
        // (the conversation ended, Vm()==null) fully resets.
        public override void OnPop()
        {
            _focused = null;
            if (Vm() == null) Reset();
        }

        /// <summary>Full reset on push / real close: clear both markers (base) plus any subclass conversation
        /// state (dialogue's fade subscription). Subclasses override and call <c>base.Reset()</c>.</summary>
        protected virtual void Reset() { _focused = null; _spoken = null; }

        public override void OnUpdate()
        {
            var vm = Vm();
            if (vm == null) return;
            PreUpdate(vm); // dialogue: fade-command shadowing + new-conversation marker reset
            var id = Identity(vm);
            if (id == null) return; // the VM can exist a frame before the first page/cue is pushed

            // On a new page/cue (or after a hide cleared the focus marker), point focus at the passage/cue TOP
            // SILENTLY — the delivery speech below is the announcement, and the frame differ would otherwise
            // double-speak. FocusNode is a request the navigator applies once the node is in the render (Build
            // declares it live this frame). announce:false always here.
            if (!SameId(id, _focused))
            {
                _focused = id;
                Navigation.Active?.FocusNode(HomeNode(vm, id), announce: false);
            }

            // Speak once delivered (panel visible and not faded, per Deliverable). Once per page/cue, QUEUED.
            if (Deliverable(vm) && !SameId(id, _spoken))
            {
                _spoken = id;
                SpeakLine(vm, id);
            }

            PostUpdate(vm); // dialogue: bare-digit answer quick-select
        }

        /// <summary>The current page/cue's stable identity, or null before the first one is pushed.</summary>
        protected abstract TId Identity(TVm vm);

        /// <summary>The graph node to home focus onto for <paramref name="id"/> (the passage top / cue row).</summary>
        protected abstract ControlId HomeNode(TVm vm, TId id);

        /// <summary>Speak the freshly-delivered line for <paramref name="id"/>, QUEUED.</summary>
        protected abstract void SpeakLine(TVm vm, TId id);

        /// <summary>Whether <paramref name="a"/> is the same page/cue as the marker <paramref name="b"/>. Default
        /// reference identity; dialogue overrides to key on the stable BlueprintCue.</summary>
        protected virtual bool SameId(TId a, TId b) => ReferenceEquals(a, b);

        /// <summary>Whether the line is deliverable this frame. Default always; dialogue gates on the panel being
        /// visible and not mid-cue faded so a cutscene-hidden cue isn't spoken.</summary>
        protected virtual bool Deliverable(TVm vm) => true;

        /// <summary>Pre-identity per-frame hook (dialogue's fade shadowing + new-conversation reset). No-op default.</summary>
        protected virtual void PreUpdate(TVm vm) { }

        /// <summary>Post-step per-frame hook (dialogue's number-select). No-op default.</summary>
        protected virtual void PostUpdate(TVm vm) { }
    }
}
