// JunoVoice - text-to-speech helper for Manydocks' assistant Juno.
// Copyright (C) 2026 Denish Chairany
//
// This program is free software: you can redistribute it and/or modify it under the terms of the GNU General Public
// License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later
// version. This program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the
// implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU General Public License
// (COPYING.txt next to this file, or https://www.gnu.org/licenses/gpl-3.0.html) for more details.
//
// Why a separate program: the Kokoro voice needs espeak-ng (GPL-3.0-or-later), which sherpa-onnx compiles into its
// full sherpa-onnx-c-api.dll. JunoVoice loads that DLL; Manydocks itself only loads the "no-tts" build (Apache-2.0) and
// talks to JunoVoice through standard input/output, as two separate programs.
//
// Usage: JunoVoice.exe <voice folder> [threads]
//   <voice folder>\bin\onnxruntime.dll, <voice folder>\tts\sherpa-onnx-c-api.dll, <voice folder>\kokoro-en-v0_19\...
// Protocol (one request per line on stdin, UTF-8):
//   WARM                          -> "OK <load ms>\n"                       (load the model now)
//   SPEAK <speaker> <speed> <b64> -> "WAV <bytes>\n" + WAV file bytes      (b64 = Base64 of the UTF-8 text)
//   errors                        -> "ERR <message>\n"
// The program exits when stdin closes.
// Build: csc /target:exe /platform:x64 /out:JunoVoice.exe JunoVoice.cs   (.NET Framework 4.x)
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace JunoVoice {
    internal static class Program {
        private const string Dll = "sherpa-onnx-c-api.dll";
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr SherpaOnnxCreateOfflineTts(IntPtr config);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr SherpaOnnxOfflineTtsGenerate(IntPtr tts, IntPtr text, int sid, float speed);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern void SherpaOnnxDestroyOfflineTtsGeneratedAudio(IntPtr audio);

        // SherpaOnnxOfflineTtsConfig of sherpa-onnx v1.13.8 c-api.h, as a list of fields (see Layout below).
        private const string TtsSpec = @"
            model{
              vits{ p:model p:lexicon p:tokens p:data_dir f:noise_scale f:noise_scale_w f:length_scale p:dict_dir }
              i:num_threads i:debug p:provider
              matcha{ p:acoustic p:vocoder p:lexicon p:tokens p:data_dir f:noise_scale f:length_scale p:dict_dir }
              kokoro{ p:model p:voices p:tokens p:data_dir f:length_scale p:dict_dir p:lexicon p:lang }
              kitten{ p:model p:voices p:tokens p:data_dir f:length_scale }
              zipvoice{ p:tokens p:encoder p:decoder p:vocoder p:data_dir p:lexicon f:feat_scale f:t_shift f:target_rms f:guidance }
              pocket{ p:lm_flow p:lm_main p:encoder p:decoder p:text_conditioner p:vocab p:scores i:cache }
              supertonic{ p:duration p:text_encoder p:vector p:vocoder p:tts_json p:indexer p:style }
            }
            p:rule_fsts i:max_num_sentences p:rule_fars f:silence_scale";

        private static IntPtr tts;
        private static string voiceDir; private static int threads;

        private static int Main(string[] args) {
            if (args.Length < 1) { Console.Error.WriteLine("usage: JunoVoice.exe <voice folder> [threads]"); return 2; }
            voiceDir = args[0]; threads = args.Length > 1 ? Math.Max(1, Math.Min(8, Int32.Parse(args[1]))) : 4;
            var input = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
            var output = Console.OpenStandardOutput();
            string line;
            while ((line = input.ReadLine()) != null) {
                byte[] reply;
                try { reply = Handle(line.Trim()); }
                catch (Exception ex) { reply = Ascii("ERR " + ex.GetType().Name + ": " + ex.Message.Replace('\n', ' ') + "\n"); }
                output.Write(reply, 0, reply.Length); output.Flush();
            }
            return 0;
        }
        private static byte[] Handle(string line) {
            if (line == "WARM") { int t0 = Environment.TickCount; string err = Load(); return Ascii(err != null ? "ERR " + err + "\n" : "OK " + unchecked(Environment.TickCount - t0) + "\n"); }
            if (line.StartsWith("SPEAK ", StringComparison.Ordinal)) {
                var parts = line.Split(' ');
                if (parts.Length != 4) return Ascii("ERR bad request\n");
                int sid = Int32.Parse(parts[1]); float speed = Single.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture);
                string text = Encoding.UTF8.GetString(Convert.FromBase64String(parts[3]));
                string err = Load(); if (err != null) return Ascii("ERR " + err + "\n");
                byte[] wav = Speak(text, sid, speed);
                if (wav == null) return Ascii("ERR no audio\n");
                var head = Ascii("WAV " + wav.Length + "\n"); var all = new byte[head.Length + wav.Length];
                Buffer.BlockCopy(head, 0, all, 0, head.Length); Buffer.BlockCopy(wav, 0, all, head.Length, wav.Length); return all;
            }
            return Ascii("ERR unknown request\n");
        }
        private static string Load() {
            if (tts != IntPtr.Zero) return null;
            string bin = Path.Combine(voiceDir, "bin"), engine = Path.Combine(voiceDir, "tts"), kokoro = Path.Combine(voiceDir, "kokoro-en-v0_19");
            const uint AlteredSearchPath = 0x8;
            if (LoadLibraryEx(Path.Combine(bin, "onnxruntime.dll"), IntPtr.Zero, AlteredSearchPath) == IntPtr.Zero) return "onnxruntime.dll could not be loaded (" + Marshal.GetLastWin32Error() + ")";
            if (LoadLibraryEx(Path.Combine(engine, Dll), IntPtr.Zero, AlteredSearchPath) == IntPtr.Zero) return Dll + " could not be loaded (" + Marshal.GetLastWin32Error() + ")";
            var layout = new Layout(TtsSpec); IntPtr config = Marshal.AllocHGlobal(layout.Size); var strings = new List<IntPtr>();
            try {
                for (int i = 0; i < layout.Size; i++) Marshal.WriteByte(config, i, 0);
                Action<string, string> str = (name, value) => { var p = Utf8(value); strings.Add(p); Marshal.WriteIntPtr(config, layout.Offsets[name], p); };
                str("model.kokoro.model", Path.Combine(kokoro, "model.onnx"));
                str("model.kokoro.voices", Path.Combine(kokoro, "voices.bin"));
                str("model.kokoro.tokens", Path.Combine(kokoro, "tokens.txt"));
                str("model.kokoro.data_dir", Path.Combine(kokoro, "espeak-ng-data"));
                Marshal.WriteInt32(config, layout.Offsets["model.kokoro.length_scale"], BitConverter.ToInt32(BitConverter.GetBytes(1.0f), 0));
                Marshal.WriteInt32(config, layout.Offsets["model.num_threads"], threads); str("model.provider", "cpu");
                Marshal.WriteInt32(config, layout.Offsets["max_num_sentences"], 1); // Kokoro accepts only 1
                Marshal.WriteInt32(config, layout.Offsets["silence_scale"], BitConverter.ToInt32(BitConverter.GetBytes(0.2f), 0));
                tts = SherpaOnnxCreateOfflineTts(config);
            } finally { foreach (var p in strings) Marshal.FreeHGlobal(p); Marshal.FreeHGlobal(config); }
            return tts == IntPtr.Zero ? "the Kokoro voice could not be created" : null;
        }
        private static byte[] Speak(string text, int sid, float speed) {
            IntPtr utf = Utf8(text);
            try {
                IntPtr a = SherpaOnnxOfflineTtsGenerate(tts, utf, sid, speed);
                if (a == IntPtr.Zero) return null;
                try {
                    IntPtr samples = Marshal.ReadIntPtr(a); int n = Marshal.ReadInt32(a, 8), rate = Marshal.ReadInt32(a, 12);
                    if (samples == IntPtr.Zero || n <= 0 || rate <= 0) return null;
                    var f = new float[n]; Marshal.Copy(samples, f, 0, n); return Wav(f, rate);
                } finally { SherpaOnnxDestroyOfflineTtsGeneratedAudio(a); }
            } finally { Marshal.FreeHGlobal(utf); }
        }
        private static byte[] Wav(float[] samples, int rate) {
            int data = samples.Length * 2;
            using (var ms = new MemoryStream(44 + data)) using (var w = new BinaryWriter(ms)) {
                w.Write(Ascii("RIFF")); w.Write(36 + data); w.Write(Ascii("WAVEfmt "));
                w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
                w.Write(Ascii("data")); w.Write(data);
                foreach (float s in samples) w.Write((short)Math.Max(-32768, Math.Min(32767, Math.Round(s * 32767))));
                w.Flush(); return ms.ToArray();
            }
        }
        private static byte[] Ascii(string s) { return Encoding.ASCII.GetBytes(s); }
        private static IntPtr Utf8(string s) {
            var bytes = Encoding.UTF8.GetBytes(s ?? ""); var p = Marshal.AllocHGlobal(bytes.Length + 1);
            Marshal.Copy(bytes, 0, p, bytes.Length); Marshal.WriteByte(p, bytes.Length, 0); return p;
        }
    }

    // C struct layout (x64, natural alignment) from a field list: "p:name" pointer, "i:name" int32, "f:name" float,
    // "name{ ... }" nested struct. Field address: "outer.inner".
    internal sealed class Layout {
        internal readonly Dictionary<string, int> Offsets = new Dictionary<string, int>();
        internal readonly int Size;
        internal Layout(string spec) {
            var tokens = spec.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            int at = 0, align; var fields = new List<KeyValuePair<string, int>>();
            Size = Block(tokens, ref at, fields, out align);
            foreach (var f in fields) Offsets[f.Key] = f.Value;
        }
        private static int Block(string[] t, ref int at, List<KeyValuePair<string, int>> fields, out int align) {
            int offset = 0; align = 1;
            while (at < t.Length && t[at] != "}") {
                string tok = t[at++];
                if (tok.EndsWith("{", StringComparison.Ordinal)) {
                    string name = tok.Substring(0, tok.Length - 1); var inner = new List<KeyValuePair<string, int>>(); int a;
                    int size = Block(t, ref at, inner, out a); at++; // skip "}"
                    offset = Up(offset, a); foreach (var f in inner) fields.Add(new KeyValuePair<string, int>(name + "." + f.Key, offset + f.Value));
                    offset += size; align = Math.Max(align, a);
                } else {
                    int size = tok[0] == 'p' ? 8 : 4; string name = tok.Substring(2);
                    offset = Up(offset, size); fields.Add(new KeyValuePair<string, int>(name, offset)); offset += size; align = Math.Max(align, size);
                }
            }
            return Up(offset, align);
        }
        private static int Up(int v, int a) { return (v + a - 1) / a * a; }
    }
}
