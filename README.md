# NMKD Stable Diffusion GUI
Somewhat modular text2image GUI, initially just for Stable Diffusion.

Relies on a slightly customized fork of the InvokeAI Stable Diffusion code (formerly lstein): [Code Repo](https://github.com/n00mkrad/stable-diffusion-cust/commits/main)



## System Requirements

#### Minimum:

- **GPU:** Nvidia GPU with 4 GB VRAM, Maxwell Architecture (2014) or newer
- **RAM:** 8 GB RAM (Note: Pagefile must be enabled as swapping will occur with only 8 GB!)
- **Disk:** 12 GB (another free 2 GB for temporary files recommended)

#### Recommended:

- **GPU:** Nvidia GPU with 8 GB VRAM, Pascal Architecture (2016) or newer
- **RAM:** 16 GB RAM
- **Disk:** 12 GB on SSD (another free 2 GB for temporary files recommended)

#### Professional/DreamBooth-capable:

- **GPU:** Nvidia GPU with 24GB VRAM, Turing Architecture (2018) or newer
- **RAM:** 32 GB RAM
- **Disk:** 12 GB on NVME SSD (another free 30 GB for temporary files recommended)



## Features and How to Use Them

### Prompt Input

- **Multiple prompts at once:** Enter each prompt on a new line (newline-separated). Word wrapping does not count towards this.
- **Exclusion Words:** Put words or phrases into [square brackets] to tell the AI to exclude those things when generating images.
- **Emphasis:** Use (parentheses) to make a word/phrase more impactful, or {curly brackets} to do the opposite. You can also use ((multiple)).



### Additional Inputs

* **Load Image:** Load an initialization image that will be used together with your text prompt ("img2img")
* **Load Concept:** Load a Textual Inversion concept to apply a style or use a specific character



### Stable Diffusion Settings

* **Steps:** More steps can increase detail, but only to a certain extend. Depending on the sampler, 20-60 is a good range.
  * Has a linear performance impact: Doubling the step count means each image takes twice as long to generate.
* **Prompt Guidance (CFG Scale):** Lower values are closer to the raw output of the AI, higher values try to respect your prompt more accurately.
  * Use low values if you are happy with the AI's representation of your prompt. Use higher values if not - but going too high will degrade quality.
  * No performance impact, no matter the value.
* **Seed:** Starting value for the image generation. Allows you to create the exact same image again by using the same seed.
  * When using the same seed, the image will only be identical if you also use the same sampler and resolution (and other settings).
* **Resolution:** Adjust image size. Only values that are divisible by 64 are possible. Sizes above 512x512 can lead to repeated patterns.
* **Sampler:** Changes the way images are sampled. Euler Ancestral is default because it's fast and tends to look good even with few steps.
* **Generate Seamless Images:** Generates seamless/tileable images, very useful for making game textures or repeating backgrounds.



### Image Viewer

* **Review current images:** Use the scroll wheel while hovering over the image to go to the previous/next image.
* **Slideshow:** The image viewer always shows the newest generated image if you haven't manually changed it in the last 3 seconds.
* **Context Menu:** Right-click into the image area to show more options.
* **Pop-Up Viewer:** Click into the image area to open the current image in a floating window.
  * Use the mouse wheel to change the window's size (zoom), right-click for more options, double-click to toggle fullscreen.



### Settings Button (Top Bar)

* **Low Memory Mode:** Use "optimizedSD" implementation that is very slow, but requires less VRAM. Not recommended unless you need it.
* **Use Full Precision:** Use FP32 instead of FP16 math, which requires more VRAM but can fix certain compatibility issues.
* **Unload Model After Each Generation:** Completely unload Stable Diffusion after images are generated.
* **Stable Diffusion Model File:** Select the model file to use for image generation.
  * Included models are located in `Data/models`. You can add your own folder paths by clicking on "Folders...".
* **CUDA Device:** Allows your to specify the GPU to run the AI on, or set it to run on the CPU (very slow).
* **Image Output Folder:** Set the folder where your generated images will be saved.
* **Create a Subfolder for Each Prompt:** If enabled, images will be saved in a folder named after their prompt.
* **Metadata to Include in Filename:** Specify which information should be included in the filename.
* **When Running Multiple Prompts, Use Same Starting Seed for All of Them:** If enabled, the seed resets to the starting value for every new prompt. If disabled, the seed will be incremented by 1 after each iteration, being sequential until all prompts/iterations have been generated.
* **When Post-Processing Is Enabled, Also Save Un-Processed Image:** When enabled, both the "raw" and the post-processed image will be saved.
* **Advanced Mode:** Increases the limits of the sliders in the main window. Not very useful most of the time unless you really need those high values.
* **Notify When Image Generation Has Finished:** Play a sound, show a notification, or do both if image generation finishes in background.



### Logs Button (Top Bar)

* **Open Logs Folder:** Opens the log folder of the current session. The application deletes logs older than 3 days on every startup.
* **Copy <logname.txt>**: Copy one of the log files generated in the current session to clipboard.



### Installer Button (Top Bar)

* **Installation Status:** Shows which modules are installed (checkboxes are not interactive and only indicate if a module is installed correctly!).
* **Redownload SD Model:** Re-downloads Stable Diffusion 1.4 model (4 GB) from Google Storage servers.
* **Re-Install SD Code:** Re-installs the Stable Diffusion code from its repository. Can fix some issues related to file paths.
* **Re-Install Upscalers:** (Re-)Installs upscaling files (RealESRGAN, GFPGAN, CodeFormer, including model files).
* **(Re-)Install:** Installs everything. Skips already installed components.
* **Uninstall:** Removes everything except for Conda which is included and needed for a re-installation.



### Developer Tools Button (Top Bar)

* **Open Dream.py CLI:** Use Stable Diffusion in command-line interface
* **Merge Models:** Allows you to merge/blend two models. The percentage numbers represent their respective weight.
* **Prune Models:** Allows you to reduce the size of models by removing data that's not needed for image generation.
* **View Log In Realtime:** Opens a separate window that shows all log output, including messages that are not shown in the normal log box.
* **Train DreamBooth Model:** Allows you to train a model on a specific object or character using only 3-20 images of it.



### Post-Processing Button (Top Bar)

* **Upscaling:** Set RealESRGAN upscaling factor.
* **Face Restoration:** Enable GFPGAN or CodeFormer for face restoration.



### Bottom Bar Buttons

* **Generate:** Start AI image generation (or cancel if it's already running).
* **Prompt Queue Button:** Right-click to add the current settings to the queue, or left-click to manage the queued entries.
* **Prompt History Button:** View recent prompts, load them into the main window, search or clear history, or disable it.
* **Image Deletion Button:** Delete either the image that is being viewed currently, or all images from the current batch.
* **Open Folder Button:** Opens the (root) image output folder.
* **Left/Right Buttons:** Show the previous or next image from the current batch.



## Hotkeys (Main Window)

- **CTRL+G:** Run Image Generation (or Cancel if already running)
- **CTRL+M:** Show Model Quick Switcher
- **CTRL+PLUS:** Toggle Prompt Textbox Size
- **CTLR+DEL:** Delete currently viewed image
- **CTRL+SHIFT+DEL:** Delete all generated images (of the current batch)
- **CTRL+O:** Open currently viewed image
- **CTRL+SHIFT+O:** Show current image in its folder
- **CTRL+V:** Paste Image (If clipboard contains a bitmap)
- **CTRL+Q:** Quit
- **CTRL+Scroll:** Change font size (only while mouse is over prompt text field)
- **F1:** Open Help (Currently links to GitHub Readme)
- **F12:** Open Settings
