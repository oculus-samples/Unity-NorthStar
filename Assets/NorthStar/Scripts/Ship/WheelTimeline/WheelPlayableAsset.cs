// Copyright (c) Meta Platforms, Inc. and affiliates.
using UnityEngine;
using UnityEngine.Playables;

namespace NorthStar
{
    public class WheelPlayableAsset : PlayableAsset
    {
        public WheelPlayableBehaviour template;
        //public float OverrideValue;
        //public bool Override;
        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            var playable = ScriptPlayable<WheelPlayableBehaviour>.Create(graph, template);
            //var behaviour = playable.GetBehaviour();
            //behaviour.OverrideValue = OverrideValue;
            //behaviour.Override = Override;
            return playable;
        }

    }
}