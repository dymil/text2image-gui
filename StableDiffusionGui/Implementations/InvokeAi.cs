﻿using StableDiffusionGui.Data;
using StableDiffusionGui.Extensions;
using StableDiffusionGui.Installation;
using StableDiffusionGui.Io;
using StableDiffusionGui.Main;
using StableDiffusionGui.MiscUtils;
using StableDiffusionGui.Os;
using StableDiffusionGui.Ui;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace StableDiffusionGui.Implementations
{
    internal class InvokeAi
    {
        public static int Pid = -1;

        public enum FixAction { Upscale, FaceRestoration }

        public static Dictionary<string, string> PostProcessMovePaths = new Dictionary<string, string>();
        public static EasyDict<string, string> EmbeddingsFilesTriggers = new EasyDict<string, string>(); // Key = Filename without ext, Value = Trigger

        private static OperationOrder _opOrder = new OperationOrder();
        private static Action<OperationOrder.LoopAction> _loopIterations, _loopPrompts, _loopScales, _loopSteps, _loopInits, _loopInitStrengths, _loopLoraWeights;

        public static async Task Run(TtiSettings s, string outPath)
        {
            try
            {
                string vae = s.Vae.NullToEmpty().Replace("None", ""); // VAE model name
                var allModels = Models.GetModelsAll();
                var cachedModels = allModels.Where(m => m.Type == Enums.Models.Type.Normal).ToList();
                var cachedModelsVae = Models.GetVaes();
                Model modelFile = TtiUtils.CheckIfModelExists(s.Model, Enums.StableDiffusion.Implementation.InvokeAi);
                Model vaeFile = Models.GetModel(cachedModelsVae, vae);
                if (TextToImage.Canceled) return;

                cachedModels[cachedModels.IndexOf(cachedModels.Where(m => m.FullName == modelFile.FullName).First())].SetArch(s.ModelArch);

                OrderedDictionary initImages = s.InitImgs != null && s.InitImgs.Length > 0 ? await TtiUtils.CreateResizedInitImagesIfNeeded(s.InitImgs.ToList(), s.Res, s.ResizeGravity) : null;

                long startSeed = s.Seed;
                s.Prompts = s.Prompts.Select(p => InvokeAiUtils.GetCombinedPrompt(p, s.NegativePrompt, s.Loras)).ToArray(); // Apply negative prompt

                var argLists = new List<EasyDict<string, string>>(); // List of all args for each command
                var args = new EasyDict<string, string>(); // List of args for current command
                args["prompt"] = "";
                args["default"] = Args.InvokeAi.GetDefaultArgsCommand();
                args["upscale"] = Args.InvokeAi.GetUpscaleArgs();
                args["facefix"] = Args.InvokeAi.GetFaceRestoreArgs();
                args["seamless"] = Args.InvokeAi.GetSeamlessArg(s.SeamlessMode);
                args["symmetry"] = Args.InvokeAi.GetSymmetryArg(s.SymmetryMode);
                args["hiresFix"] = s.HiresFix ? "--hires_fix" : "";
                args["perlin"] = s.Perlin > 0f ? $"--perlin {s.Perlin.ToStringDot()}" : "";
                args["threshold"] = s.Threshold > 0 ? $"--threshold {s.Threshold}" : "";
                args["clipSegMask"] = (s.ImgMode == Enums.StableDiffusion.ImgMode.TextMask && !string.IsNullOrWhiteSpace(s.ClipSegMask)) ? $"-tm {s.ClipSegMask.Wrap()}" : "";
                args["debug"] = s.AppendArgs;
                args["res"] = $"-W {s.Res.Width} -H {s.Res.Height}";
                args["sampler"] = $"-A {s.Sampler.ToString().Lower()}";

                if (initImages == null || initImages.Count <= 0)
                    _opOrder.LoopOrder.Remove(OperationOrder.LoopAction.InitStrengths);

                if (!s.Loras.Any())
                    _opOrder.LoopOrder.Remove(OperationOrder.LoopAction.LoraWeight);

                List<string> processedPrompts = null;
                TextToImage.CurrentTaskSettings.ProcessedAndRawPrompts = new EasyDict<string, string>();

                int currentIteration = 0;
                float currentLoraWeight = 1.0f;

                void NextAction(OperationOrder.LoopAction thisAction)
                {
                    if (_opOrder.SeedResetActions.Contains(thisAction))
                        s.Seed = startSeed;

                    if (_opOrder.SeedIncrementActions.Contains(thisAction) && !s.LockSeed)
                        s.Seed++;

                    RunNextLoopAction(thisAction, () => FinalAction());
                }

                void FinalAction()
                {
                    string currPrompt = processedPrompts[currentIteration];

                    if (s.Loras != null && s.Loras.Count == 1)
                        foreach (var lora in s.Loras)
                            currPrompt = currPrompt.Replace($"{lora.Key}Weight", currentLoraWeight.ToStringDot("0.0###"));

                    args["prompt"] = currPrompt.Wrap();
                    argLists.Add(new EasyDict<string, string>(args));
                }

                _loopPrompts = (thisAction) =>
                {
                    foreach (string prompt in s.Prompts)
                    {
                        processedPrompts = PromptWildcardUtils.ApplyWildcardsAll(prompt, s.Iterations, false);
                        TextToImage.CurrentTaskSettings.ProcessedAndRawPrompts = new EasyDict<string, string>(processedPrompts.Distinct().ToDictionary(x => x, x => prompt));
                        NextAction(thisAction);
                    }
                };

                _loopIterations = (thisAction) =>
                {
                    currentIteration = 0;

                    for (int i = 0; i < s.Iterations; i++)
                    {
                        args["seed"] = $"-S {s.Seed}";
                        NextAction(thisAction);
                        currentIteration++;
                    }
                };

                _loopScales = (thisAction) =>
                {
                    foreach (float scale in s.ScalesTxt)
                    {
                        args["scale"] = $"-C {scale.Clamp(0.01f, 1000f).ToStringDot()}";
                        NextAction(thisAction);
                    }
                };

                _loopSteps = (thisAction) =>
                {
                    foreach (int stepCount in s.Steps)
                    {
                        args["steps"] = $"-s {stepCount}";

                        NextAction(thisAction);
                    }
                };

                _loopInits = (thisAction) =>
                {
                    if (initImages == null) // No init image(s)
                    {
                        args["initImg"] = "";
                        args["initStrength"] = "";
                        NextAction(thisAction);
                    }
                    else // With init image(s)
                    {
                        foreach (string initImg in initImages.Values)
                        {
                            args["initImg"] = $"-I {initImg.Wrap()}";
                            NextAction(thisAction);
                        }
                    }
                };

                _loopInitStrengths = (thisAction) =>
                {
                    foreach (float strength in s.InitStrengthsReverse)
                    {
                        args["initStrength"] = s.ImgMode != Enums.StableDiffusion.ImgMode.InitializationImage ? "-f 1.0" : $"-f {strength.ToStringDot("0.###")}"; // Lock to 1.0 when using inpainting

                        if (s.ImgMode == Enums.StableDiffusion.ImgMode.ImageMask)
                            args["inpaintMask"] = $"-M {Inpainting.MaskedImagePath.Wrap()}";
                        else if (s.ImgMode == Enums.StableDiffusion.ImgMode.Outpainting)
                            args["inpaintMask"] = "--force_outpaint";
                        else
                            args["inpaintMask"] = "";

                        NextAction(thisAction);
                    }
                };

                _loopLoraWeights = (thisAction) =>
                {
                    foreach (float weight in s.Loras.First().Value)
                    {
                        currentLoraWeight = weight;
                        NextAction(thisAction);
                    }
                };

                RunLoopAction(_opOrder.LoopOrder.First());

                Logger.Log($"Running Stable Diffusion - {s.Iterations} Iterations, {s.Steps.Length} Steps, Scales {(s.ScalesTxt.Length < 4 ? string.Join(", ", s.ScalesTxt.Select(x => x.ToStringDot())) : $"{s.ScalesTxt.First()}->{s.ScalesTxt.Last()}")}, {s.Res.AsString()}, Starting Seed: {startSeed}", false, Logger.LastUiLine.EndsWith("..."));

                string modelsChecksumStartup = InvokeAiUtils.GetModelsHash(cachedModels);
                string argsStartup = Args.InvokeAi.GetArgsStartup(cachedModels);
                string newStartupSettings = $"{argsStartup} {modelsChecksumStartup} {Config.Instance.CudaDeviceIdx} {Config.Instance.ClipSkip}"; // Check if startup settings match - If not, we need to restart the process

                Logger.Log(GetImageCountLogString(initImages, s.InitStrengthsReverse, s.Prompts, s.Iterations, s.Steps, s.ScalesTxt, argLists));

                Logger.Clear(Constants.Lognames.Sd);
                bool restartedInvoke = false; // Will be set to true if InvokeAI was not running before

                if (!TtiProcess.IsAiProcessRunning || (TtiProcess.IsAiProcessRunning && TtiProcess.LastStartupSettings != newStartupSettings))
                {
                    InvokeAiUtils.WriteModelsYamlAll(cachedModels, cachedModelsVae, s.ModelArch);
                    await SetModel(modelFile, vaeFile, true);
                    if (TextToImage.Canceled) return;

                    Logger.Log($"(Re)starting InvokeAI. Process running: {TtiProcess.IsAiProcessRunning} - Prev startup string: '{TtiProcess.LastStartupSettings}' - New startup string: '{newStartupSettings}'", true);

                    TtiProcess.LastStartupSettings = newStartupSettings;

                    string invokeExePath = Path.Combine(Paths.GetDataPath(), Constants.Dirs.SdVenv, "Scripts", "invokeai.exe");
                    Process py = OsUtils.NewProcess(!OsUtils.ShowHiddenCmd(), Path.Combine(Paths.GetDataPath(), Constants.Dirs.SdVenv, "Scripts", "python.exe"));
                    py.StartInfo.EnvironmentVariables["INVOKEAI_ROOT"] = InvokeAiUtils.HomePath;
                    py.StartInfo.EnvironmentVariables["INVOKE_NMKD_HIRES_MINDIM_MULT"] = Config.IniInstance.HiresFixMinimumDimensionMultiplier.ToStringDot("0.0##");
                    py.StartInfo.RedirectStandardInput = true;
                    py.StartInfo.WorkingDirectory = Paths.GetDataPath();
                    py.StartInfo.Arguments = $"{invokeExePath.Wrap()} --model {InvokeAiUtils.GetMdlNameForYaml(modelFile, vaeFile)} -o {outPath.Wrap(true)} {argsStartup}";

                    foreach (var pair in TtiUtils.GetEnvVarsSd(false, Paths.GetDataPath()))
                        py.StartInfo.EnvironmentVariables[pair.Key] = pair.Value;

                    TextToImage.CurrentTask.Processes.Add(py);
                    Logger.Log($"{py.StartInfo.FileName} {py.StartInfo.Arguments}", true);

                    if (!OsUtils.ShowHiddenCmd())
                    {
                        py.OutputDataReceived += (sender, line) => { TtiProcessOutputHandler.LogOutput(line.Data); };
                        py.ErrorDataReceived += (sender, line) => { TtiProcessOutputHandler.LogOutput(line.Data, true); };
                    }

                    if (TtiProcess.CurrentProcess != null)
                    {
                        TtiProcess.ProcessExistWasIntentional = true;
                        OsUtils.KillProcessTree(TtiProcess.CurrentProcess.Id);
                    }

                    TtiProcessOutputHandler.Reset();

                    string logMdl = modelFile.FormatIndependentName.Trunc(vae.IsNotEmpty() ? 40 : 80).Wrap();
                    string logVae = vaeFile == null ? "" : vaeFile.FormatIndependentName.Trunc(40).Wrap();
                    Logger.Log($"Loading Stable Diffusion with model {logMdl}{(string.IsNullOrWhiteSpace(logVae) ? "" : $" and VAE {logVae}")}...");

                    TtiProcess.CurrentProcess = py;
                    TtiProcess.ProcessExistWasIntentional = false;

                    restartedInvoke = true;
                    py.Start();
                    OsUtils.AttachOrphanHitman(py);
                    TtiProcess.CurrentStdInWriter = new NmkdStreamWriter(py);

                    if (!OsUtils.ShowHiddenCmd())
                    {
                        py.BeginOutputReadLine();
                        py.BeginErrorReadLine();
                    }

                    Task.Run(() => TtiProcess.CheckStillRunning());

                    string embeddingMsg = await Logger.WaitForMessageAsync(Constants.LogMsgs.Invoke.TiTriggers, true, true);
                    InvokeAiUtils.LoadEmbeddingTriggerTable(embeddingMsg);
                }
                else
                {
                    TtiProcessOutputHandler.Reset();
                    await SetModel(modelFile, vaeFile);
                    TextToImage.CurrentTask.Processes.Add(TtiProcess.CurrentProcess);
                }

                bool noCommandsSent = true;

                foreach (var argList in argLists)
                {
                    if (TextToImage.Canceled)
                        break;

                    if (!InvokeAiUtils.ValidateCommand(argList, s.Res))
                        continue;

                    argList["prompt"] = InvokeAiUtils.FixPromptEmbeddingTriggers(argList["prompt"]);
                    string command = string.Join(" ", argList.Where(entry => entry.Value.IsNotEmpty()).Select(argEntry => argEntry.Value));
                    await TtiProcess.WriteStdIn(command);
                    noCommandsSent = false;
                }

                if (noCommandsSent)
                    TextToImage.Cancel("No valid commands.", false, restartedInvoke ? TextToImage.CancelMode.ForceKill : TextToImage.CancelMode.DoNotKill);
            }
            catch (Exception ex)
            {
                Logger.Log($"Unhandled Stable Diffusion Error: {ex.Message}");
                Logger.Log(ex.StackTrace, true);
            }
        }

        static void RunNextLoopAction(OperationOrder.LoopAction previousAction, Action doneAction)
        {
            int actionIndex = _opOrder.LoopOrder.IndexOf(previousAction);

            if (actionIndex + 1 < _opOrder.LoopOrder.Count)
                RunLoopAction(_opOrder.LoopOrder.ElementAt(actionIndex + 1));
            else
                doneAction(); // End
        }

        static void RunLoopAction(OperationOrder.LoopAction a)
        {
            if (a == OperationOrder.LoopAction.Prompt) _loopPrompts(a);
            if (a == OperationOrder.LoopAction.Iteration) _loopIterations(a);
            if (a == OperationOrder.LoopAction.Scale) _loopScales(a);
            if (a == OperationOrder.LoopAction.Step) _loopSteps(a);
            if (a == OperationOrder.LoopAction.InitImg) _loopInits(a);
            if (a == OperationOrder.LoopAction.InitStrengths) _loopInitStrengths(a);
            if (a == OperationOrder.LoopAction.LoraWeight) _loopLoraWeights(a);
        }

        public static string GetImageCountLogString(OrderedDictionary initImages, float[] initStrengths, string[] prompts, int iterations, int[] steps, float[] scales, List<EasyDict<string, string>> argLists)
        {
            string initsStr = initImages != null ? $" and {initImages.Count} Image{(initImages.Count != 1 ? "s" : "")} Using {initStrengths.Length} Strength{(initStrengths.Length != 1 ? "s" : "")}" : "";
            string log = $"{prompts.Length} Prompt{(prompts.Length != 1 ? "s" : "")} * {iterations} Image{(iterations != 1 ? "s" : "")} * {steps.Length} Step Value{(steps.Length != 1 ? "s" : "")} * {scales.Length} Scale{(scales.Length != 1 ? "s" : "")}{initsStr} = {argLists.Count} Images Total";

            if (ConfigParser.UpscaleAndSaveOriginals())
                log += $" ({argLists.Count * 2} With Post-processed Images)";

            return $"{log}.";
        }

        public static void RunCli(string outPath, string vaePath)
        {
            if (Program.Busy)
                return;

            TextToImage.Canceled = false;
            var allModels = Models.GetModelsAll();
            var cachedModels = allModels.Where(m => m.Type == Enums.Models.Type.Normal).ToList();
            var cachedModelsVae = allModels.Where(m => m.Type == Enums.Models.Type.Vae).ToList();
            Model modelFile = TtiUtils.CheckIfModelExists(Config.Instance.Model, Enums.StableDiffusion.Implementation.InvokeAi);
            Model vaeFile = Models.GetModel(cachedModelsVae, Path.GetFileName(vaePath));

            if (modelFile == null)
                return;

            InvokeAiUtils.WriteModelsYamlAll(cachedModels, cachedModelsVae, Enums.Models.SdArch.Automatic, true);
            if (TextToImage.Canceled) return;

            string batPath = Path.Combine(Paths.GetSessionDataPath(), "invoke.bat");

            string batText = $"@echo off\n" +
                $"title Stable Diffusion CLI (InvokeAI)\n" +
                $"cd /D {Paths.GetDataPath().Wrap()}\n" +
                $"{TtiUtils.GetEnvVarsSdCommand()}\n" +
                $"SET \"INVOKEAI_ROOT={InvokeAiUtils.HomePath}\"\n" +
                $"SET \"INVOKE_NMKD_HIRES_MINDIM_MULT={Config.IniInstance.HiresFixMinimumDimensionMultiplier.ToStringDot("0.0##")}\"\n" +
                $"{Constants.Files.VenvActivate} && " +
                $"python {Path.Combine(Paths.GetDataPath(), Constants.Dirs.SdVenv, "Scripts", "invoke.exe").Wrap()} --model {InvokeAiUtils.GetMdlNameForYaml(modelFile, vaeFile)} -o {outPath.Wrap(true)} {Args.InvokeAi.GetArgsStartup(cachedModels)}";

            File.WriteAllText(batPath, batText);
            Process cli = Process.Start(batPath);
            OsUtils.AttachOrphanHitman(cli);
        }

        public static void StartCmdInSdEnv()
        {
            Process.Start("cmd", $"/K title Env: {Constants.Dirs.SdVenv} && cd /D {Paths.GetDataPath().Wrap()} && {TtiUtils.GetEnvVarsSdCommand(true, Paths.GetDataPath())} && {Constants.Files.VenvActivate}");
        }

        /// <summary> Run InvokeAI post-processing (!fix) </summary>
        /// <returns> Successful or not </returns>
        public static async Task<bool> RunFix(string imgPath, List<FixAction> actions)
        {
            if (Program.Busy)
            {
                UiUtils.ShowMessageBox("Can't run post-processing while the program is still busy.");
                return false;
            }

            if (!InstallationStatus.HasSdUpscalers())
            {
                UiUtils.ShowMessageBox("Upscalers are not installed. You can install them in the installer window.");
                return false;
            }

            if (TtiProcess.CurrentStdInWriter == null)
            {
                UiUtils.ShowMessageBox("Can't run post-processing when Stable Diffusion is not loaded.");
                return false;
            }

            try
            {
                Program.SetState(Program.BusyState.PostProcessing);

                Logger.Log($"InvokeAI !fix: {string.Join(", ", actions.Select(x => x.ToString()))}", true);

                string tempPath = IoUtils.GetAvailablePath(Path.Combine(Paths.GetSessionDataPath(), $"postproc{FormatUtils.GetUnixTimestamp()}.png"));
                File.Copy(imgPath, tempPath);
                string suffix = $"{(actions.Contains(FixAction.Upscale) ? ".upscale" : "")}{(actions.Contains(FixAction.FaceRestoration) ? ".facefix" : "")}";
                PostProcessMovePaths.Add(Path.GetFileNameWithoutExtension(tempPath), IoUtils.FilenameSuffix(imgPath, suffix));

                List<string> args = new List<string> { "!fix", tempPath.Wrap(true) };

                if (actions.Contains(FixAction.Upscale))
                    args.Add(Args.InvokeAi.GetUpscaleArgs(true));

                if (actions.Contains(FixAction.FaceRestoration))
                    args.Add(Args.InvokeAi.GetFaceRestoreArgs(true));

                bool success = await TtiProcess.WriteStdIn(string.Join(" ", args), 0, true);

                if (!success)
                    throw new Exception("Can't interact with process. Possibly it is not running?");
                else
                    return true;
            }
            catch (Exception ex)
            {
                Logger.Log($"Error: {ex.Message}");
                Logger.Log(ex.StackTrace, true);
                Program.SetState(Program.BusyState.Standby);
                return false;
            }
        }

        public static async Task SetModel(Model mdl, Model vae, bool initial = false)
        {
            if (mdl.Format == Enums.Models.Format.Diffusers)
                Models.SetDiffusersClipSkip(mdl, Config.Instance.ClipSkip);

            if (initial) // No need to run !switch when loading InvokeAI for the first time
                return;

            await TtiProcess.WriteStdIn($"!clear");
            await TtiProcess.WriteStdIn($"!switch {InvokeAiUtils.GetMdlNameForYaml(mdl, vae)}");
        }

        public static async Task Cancel()
        {
            List<string> lastLogLines = Logger.GetLastLines(Constants.Lognames.Sd, 15);

            if (lastLogLines.Where(x => x.Contains("%|") || x.Contains("error occurred")).Any()) // Only attempt a soft cancel if we've been generating anything
                await WaitForCancel();
            else // This condition should be true if we cancel while it's still initializing, so we can just force kill the process
                TtiProcess.KillAll();
        }

        public static void SendCtrlC()
        {
            if (Pid >= 0)
                OsUtils.SendCtrlC(Pid);
        }

        private static async Task WaitForCancel()
        {
            Program.MainForm.runBtn.SetEnabled(false);

            SendCtrlC();
            SendCtrlC();
            await Task.Delay(500);

            while (true)
            {
                Logger.Entry lastLogEntry = Logger.GetLastEntries(Constants.Lognames.Sd, 1).FirstOrDefault();

                if (lastLogEntry == null)
                    break;

                var msSinceLastMsg = (DateTime.Now - lastLogEntry.TimeDequeue).TotalMilliseconds;

                if (msSinceLastMsg < 200)
                {
                    SendCtrlC();
                    await Task.Delay(250);
                }
                else if (msSinceLastMsg > 2000)
                {
                    break;
                }

                await Task.Delay(200);
            }

            Program.MainForm.runBtn.SetEnabled(true);
        }
    }
}
