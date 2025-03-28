// Copyright (c) Meta Platforms, Inc. and affiliates.
/*
* Copyright (c) Meta Platforms, Inc. and affiliates.
* All rights reserved.
*
* Licensed under the Oculus SDK License Agreement (the "License");
* you may not use the Oculus SDK except in compliance with the License,
* which is provided at the time of installation or download, or which
* otherwise accompanies this software in either electronic or hard copy form.
*
* You may obtain a copy of the License at
*
* https://developer.oculus.com/licenses/oculussdk/
*
* Unless required by applicable law or agreed to in writing, the Oculus SDK
* distributed under the License is distributed on an "AS IS" BASIS,
* WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
* See the License for the specific language governing permissions and
* limitations under the License.
*/

using UnityEngine;

/// <summary>
/// Plays a series of clips on repeat with various randomization options
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class RepeatSound : MonoBehaviour
{
    public AudioClip[] Clips;
    public float RepeatRate = 2.0f;
    public float RepeatRateRandomization = 0.0f;
    public float PitchRandomizationSemitones = 1.0f;
    public float VolumeRandomizationDb = 3.0f;
    public bool NoRepeat = true;

    private AudioSource m_source;
    private float m_timer = 0.0f;
    private int m_clipIndexPrev = 0;
    private float m_repeatPeriod = 0.0f;

    private void Start()
    {
        m_source = GetComponent<AudioSource>();
    }

    private void Update()
    {
        m_timer += Time.deltaTime;
        if (m_timer > m_repeatPeriod)
        {
            m_timer = 0.0f;
            int clipIndex;
            if (NoRepeat)
            {
                clipIndex = Random.Range(0, Clips.Length - 1);
                if (clipIndex >= m_clipIndexPrev)
                {
                    ++clipIndex;
                }
            }
            else
            {
                clipIndex = Random.Range(0, Clips.Length);
            }

            m_clipIndexPrev = clipIndex;
            m_source.clip = Clips[clipIndex];
            var pitchSemitones = Random.Range(-PitchRandomizationSemitones / 2, PitchRandomizationSemitones / 2);
            m_source.pitch = Mathf.Pow(2, pitchSemitones / 12);
            var volumeDb = Random.Range(-VolumeRandomizationDb, 0.0f);
            m_source.volume = Mathf.Pow(10.0f, volumeDb / 20.0f);
            m_source.Play();

            var newRepeatRate = RepeatRate + Random.Range(-RepeatRateRandomization / 2, RepeatRateRandomization / 2);
            m_repeatPeriod = 1.0f / newRepeatRate;
        }

    }
}
