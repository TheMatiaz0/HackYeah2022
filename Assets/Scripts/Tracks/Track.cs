﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Tracks
{
    public class Track : MonoBehaviour
    {
        [SerializeField] private string trackKey;
        [SerializeField] private float scale = 1f;
        [SerializeField] private float offset;
        [SerializeField] private GameObject notePrefab;
        [SerializeField] private KeyCode keyCode;
        [SerializeField] private float threshold;
        [SerializeField, Range(0, 1)] private float minimumPositiveAccuracy = 0.8f;
        [SerializeField] private ParticleSystem particleSystem;
        [SerializeField] private Animator telegrafAnim;
        [SerializeField] private AudioClip startBeep, holdBeep, endBeep;
        [SerializeField] private AudioSource morseSource;
        [SerializeField] private AudioClip missSound;
        [SerializeField] private AudioSource soundSource;


        private List<Note> _notes;
        private AudioSource _musicSource;

        private Coroutine _spawningCoroutine;

        private int _currentNoteIndex = 0;
        private Note CurrentNote => _currentNoteIndex < _notes.Count ? _notes[_currentNoteIndex] : null;

        private List<GameObject> _noteObjects = new();

        private bool _started = false;

        public int Combo { get; private set; }

        
        private void Start()
        {
            _notes = TrackManager.Current.Tracks[trackKey];
            _musicSource = GetComponent<AudioSource>();

            Time.timeScale = 1f;

            StartSpawning();

            particleSystem.Stop();
        }

        private void StartSpawning()
        {
            Invoke(nameof(OffsetStart), offset);
            _spawningCoroutine = StartCoroutine(Spawner());
        }

        private void OffsetStart()
        {
            _started = true;
            _musicSource.Play();
        }

        private void Spawn(float duration)
        {
            var go = Instantiate(notePrefab, transform);
            go.transform.localPosition = new Vector3(0, offset * scale, 0);
            go.transform.localScale = new Vector3(1f, duration * scale, 1);
            _noteObjects.Add(go);
        }

        private IEnumerator Spawner()
        {
            foreach (var (note, i) in _notes.Select((x, i) => (x, i)))
            {
                yield return new WaitForSeconds(note.StartDeltaTime + (note.Duration / 2));
                Spawn(note.Duration);
                yield return new WaitForSeconds(note.Duration / 2);
            }
        }

        private float _timer;

        private void Update()
        {
            MoveNotes();
            HandleInput();
            HandleSounds();
            
            telegrafAnim.SetBool("Holding", Input.GetKey(keyCode));

            if (!_started) return;
            _timer += Time.deltaTime;

            if (CurrentNote != null && _timer >= CurrentNote.StartTime + (CurrentNote.Duration * 0.5))
            {
                Debug.Log("NEXT");
                if (_currentNoteIndex - 1 > _finishedIndex)
                {
                    NoteEnd(0);
                }

                _currentNoteIndex++;

                if (_currentNoteIndex >= _notes.Count)
                {
                    GameManager.Current.GameEnd(true);
                }
            }

        }

        private void MoveNotes()
        {
            foreach (var (go, i) in _noteObjects.Select((x, i) => (x, i)))
            {
                go.transform.localPosition += new Vector3(0, -Time.deltaTime * scale, 0);
            }
        }

        private Coroutine _mouseRoutine;
        private void HandleSounds()
        {
            if (Input.GetKeyDown(keyCode) && _mouseRoutine == null)
            {
                _mouseRoutine = StartCoroutine(HandleMorseSound());
            } else if (Input.GetKeyUp(keyCode))
            {
                if (_mouseRoutine != null)
                {
                    StopCoroutine(_mouseRoutine);
                    _mouseRoutine = null;
                    morseSource.Stop();
                }
            }
        }

        private float _accuracy;
        private bool _holding;
        private int _currentNoteIndexForInput;
        private int _finishedIndex = -1;

        
        private void HandleInput()
        {
            var idx = _holding ? _currentNoteIndexForInput : _currentNoteIndex;

            if (idx >= _notes.Count) return;

            var note = _notes[idx];

            if (!_holding && _finishedIndex >= idx && Input.GetKeyDown(keyCode))
            {
                Debug.Log($"ERR, fin: {_finishedIndex}, idx: {idx}");
                return;
            }

            if (Input.GetKeyDown(keyCode))
            {
                soundSource.PlayOneShot(startBeep);
                Debug.Log($"down {idx} {note.StartTime} {_timer}");
                var dist = Mathf.Abs(note.StartTime - _timer);
                if (dist < threshold) // / note.Duration
                {
                    _accuracy += 1 - (dist / threshold); // dist * note.Duration
                    particleSystem.Play();
                }

                if (_accuracy > minimumPositiveAccuracy)
                {
                    ComboIncrease();
                }

                _currentNoteIndexForInput = _currentNoteIndex;
                _holding = true;
            }

            if (Input.GetKeyUp(keyCode) && _holding)
            {
                // Debug.Log($"up {idx} {note.StartTime + note.Duration} {_timer}");
                var dist = Mathf.Abs(note.StartTime + note.Duration - _timer);
                if (dist < (threshold)) // / note.Duration
                {
                    _accuracy += 1 - (dist / threshold); // dist * note.Duration
                }

                particleSystem.Stop();

                _finishedIndex = idx;
                NoteEnd(_accuracy / 2);

                _accuracy = 0;
                _holding = false;
                morseSource.Stop();
                soundSource.PlayOneShot(endBeep);
                // _currentNoteIndex = Mathf.Max(_currentNoteIndexForInput + 1, _currentNoteIndex);
            }
            else if (Input.GetKeyUp(keyCode))
            {
                Debug.Log("NOT HOLDING");
            }
        }
        
        private IEnumerator HandleMorseSound()
        {
            morseSource.loop = false;
            morseSource.clip = startBeep;
            morseSource.Play();
            yield return new WaitUntil(() => !morseSource.isPlaying);
            morseSource.loop = true;
            morseSource.clip = holdBeep;
            morseSource.Play();
        }

        private void ComboIncrease()
        {
            Combo++;
            TextManager.Current.AddText();
            CameraShake.Current.Shake(Mathf.Min(Combo * .3f, 2f), Mathf.Min(Combo * .5f, 2f));
        }

        private void NoteEnd(float accuracy)
        {
            TrackManager.Current.AccuracyList.Add(accuracy);
            
            if (accuracy >= minimumPositiveAccuracy)
            {
                BattleController.Current.GoodClick();
                ComboIncrease();
            }
            else
            {
                Combo = 0;
                soundSource.PlayOneShot(missSound);
                TextManager.Current.LineFailed();
                //BattleController.Current.BadClick();
            }
        }

        private void OnGUI()
        {
            GUI.Label(new Rect(10f, 10f, 200f, 200f),
                @$"note #: {_currentNoteIndex}
note # for input: {_currentNoteIndexForInput}
finished #: {_finishedIndex}
holding: {_holding}
time: {_timer}
start time of curr note: {CurrentNote?.StartTime ?? -1}
accuracy: {_accuracy}
combo: {Combo}",
                new GUIStyle
                {
                    fontSize = 25,
                    normal = new GUIStyleState
                    {
                        textColor = Color.white
                    }
                });
        }
    }
}