﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PixelAnimator : MonoBehaviour {
    public bool canFlash = false;
    public SpriteRenderer targetGraphic;
    public PixelAnimationGroup animationGroup;
    public MonoBehaviour callbackReciever;

    PixelAnimationClip clip;
    float timeOfStartClip = 0f;
    int currentFrame = 0;
    float timeOfStartFrame = 0f;
    float timeOfLastFlash = 0f;
    float flashLength = 0f;
    int flashType = 0;

    void Update () {
        if(clip == null) {
            return;
        }

        float frameStartTime = Time.time - timeOfStartFrame;
        float clipStartTime = Time.time - timeOfStartClip;

        if(clip.clipType == PixelAnimationClipType.OneTime && clipStartTime > clip.totalClipTime) {
            if(!string.IsNullOrEmpty(clip.returnToOnEnd)) {
                PlayClip(clip.returnToOnEnd);
            }
            if(clip.callbacks.TryGetValue(clip.frames.Length, out ushort code)) {
                ((IPixelAnimationCallbackReciever)callbackReciever)?.OnRecieveCallback(code);
            }
        } else if(frameStartTime > clip.secondsPerFrames[currentFrame]) {
            PlayFrame();
        }

        if(canFlash) {
            if(Time.time < timeOfLastFlash + flashLength) {
                if(flashType == 0) {
                    if(Time.time < timeOfLastFlash + flashLength * 0.5f) {
                        targetGraphic.color = PixelAnimationManager.inst.maxFlash;
                    } else {
                        targetGraphic.color = PixelAnimationManager.inst.minFlash;
                    }
                } else if(flashType == 1) {
                    if(Time.time < timeOfLastFlash + flashLength * 0.5f) {
                        targetGraphic.color = PixelAnimationManager.inst.maxFreezeFlash;
                    } else {
                        targetGraphic.color = PixelAnimationManager.inst.minFreezeFlash;
                    }
                }
            } else {
                targetGraphic.color = Color.clear;
            }
        }
    }

    private void PlayFrame () {
        if(clip.callbacks.TryGetValue(currentFrame, out ushort code)) {
            ((IPixelAnimationCallbackReciever)callbackReciever)?.OnRecieveCallback(code);
        }
        currentFrame = Modulo(currentFrame + 1, clip.secondsPerFrames.Length);

        timeOfStartFrame = Time.time;
        targetGraphic.sprite = clip.frames[currentFrame];
    }

    private void DrawFrame () {
        targetGraphic.sprite = clip.frames[currentFrame];
    }

    public void PlayClip (string clipName) {
        PixelAnimationClip newClip = animationGroup.GetClipByName(clipName);
        if(newClip == null) {
            return;
        }

        timeOfStartClip = Time.time;
        timeOfStartFrame = Time.time;
        currentFrame = 0;
        clip = newClip;
        DrawFrame();
    }

    public void PlayClipWithoutRestart (string clipName) {
        if(clip != null) {
            if(clip.clipName == clipName) {
                return;
            }
        }

        PixelAnimationClip newClip = animationGroup.GetClipByName(clipName);
        if(newClip == null) {
            return;
        }

        timeOfStartClip = Time.time;
        timeOfStartFrame = Time.time;
        currentFrame = 0;
        clip = newClip;
        DrawFrame();
    }

    public void PlayClipIfIsLoop (string clipName) {
        if(clip != null) {
            if(clip.clipType == PixelAnimationClipType.OneTime) {
                return;
            }
            if(clip.clipName == clipName) {
                return;
            }
        }

        PixelAnimationClip newClip = animationGroup.GetClipByName(clipName);
        if(newClip == null) {
            return;
        }

        timeOfStartClip = Time.time;
        timeOfStartFrame = Time.time;
        currentFrame = 0;
        clip = newClip;
        DrawFrame();
    }

    public void PlayHitFlash (float length, int type = 0) {
        flashLength = length;
        timeOfLastFlash = Time.time;
        flashType = type;
    }

    public PixelAnimationClip GetCurrentClip () {
        return clip;
    }

    static int Modulo (int x, int m) {
        int r = x % m;
        return r < 0 ? r + m : r;
    }
}

public interface IPixelAnimationCallbackReciever {
    void OnRecieveCallback (uint code);
}
