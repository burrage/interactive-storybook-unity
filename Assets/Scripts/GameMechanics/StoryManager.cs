// StoryManager loads a scene based on a SceneDescription, including loading
// images, audio files, and drawing colliders and setting up callbacks to
// handle trigger events. StoryManager uses methods in TinkerText and
// SceneObjectManipulator for setting up these callbacks.

using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using System;
using System.Collections.Generic;

public class StoryManager : MonoBehaviour {

    // We may want to call methods on GameController or add to the task queue.
    public GameController gameController;
    public StoryAudioManager audioManager;

	public GameObject portraitGraphicsPanel;
    public GameObject portraitTextPanel;
    public GameObject landscapeGraphicsPanel;
    public GameObject landscapeTextPanel;
	public GameObject landscapeWideGraphicsPanel;
	public GameObject landscapeWideTextPanel;

    public GameObject portraitTitlePanel;
    public GameObject landscapeTitlePanel;

    private bool autoplayAudio = true;

    // Used for internal references.
    private GameObject graphicsPanel;
    private GameObject textPanel;
    private GameObject titlePanel;
    private GameObject currentStanza;

    private float graphicsPanelWidth;
    private float graphicsPanelHeight;
    private float graphicsPanelAspectRatio;
    private float titlePanelAspectRatio;

    // Variables for loading TinkerTexts.
    private float STANZA_SPACING = 20; // Matches Prefab.
    private float MIN_TINKER_TEXT_WIDTH = TinkerText.MIN_WIDTH;
    private float MAX_LANDSCAPE_GRAPHICS_WIDTH = 1400;
    private float LANDSCAPE_GRAPHICS_HEIGHT = 1270;
    private float MAX_PORTRAIT_GRAPHICS_HEIGHT = 1450;
    private float PORTRAIT_GRAPHICS_WIDTH = 1500;
    private float LANDSCAPE_TEXT_WIDTH = 1050;
    private float PORTRAIT_TEXT_HEIGHT = 750;
    private float remainingStanzaWidth = 0; // For loading TinkerTexts.
    private bool prevWordEndsStanza = false; // Know when to start new stanza.

    // Array of sentences where each sentence is an array of stanzas.
    private List<Sentence> sentences;

    // Dynamically created Stanzas.
    private List<GameObject> stanzas;
    // Dynamically created TinkerTexts specific to this scene.
    private List<GameObject> tinkerTexts;
    // Dynamically created SceneObjects, keyed by their id.
    private Dictionary<int, GameObject> sceneObjects;
    private Dictionary<string, int> sceneObjectsLabelToId;
    // The image we loaded for this scene.
    private GameObject storyImage;
    // Need to know the actual dimensions of the background image, so that we
    // can correctly place new SceneObjects on the background.
    private float storyImageWidth;
    private float storyImageHeight;
    // The (x,y) coordinates of the upper left corner of the image in relation
    // to the upper left corner of the encompassing GameObject.
    private float storyImageX;
    private float storyImageY;
    // Ratio of the story image to the original texture size.
    private float imageScaleFactor;
    private DisplayMode displayMode;

    void Start() {
        Logger.Log("StoryManager start");

        this.tinkerTexts = new List<GameObject>();
        this.stanzas = new List<GameObject>();
        this.sceneObjects = new Dictionary<int, GameObject>();
        this.sceneObjectsLabelToId = new Dictionary<string, int>();
    }

    void Update() {
        // Update whether or not we are accepting user interaction.
        Stanza.allowSwipe = !this.audioManager.IsPlaying();
    }

    // Main function to be called by GameController.
    // Passes in a description received over ROS or hardcoded.
    // LoadScene is responsible for loading all resources and putting them in
    // place, and attaching callbacks to created GameObjects, where these
    // callbacks involve functions from SceneManipulatorAPI.
    public void LoadPage(SceneDescription description) {
        this.setDisplayMode(description.displayMode);
        this.resetPanelSizes();    
        // Load audio.
        this.audioManager.LoadAudio(description.audioFile);

        if (description.isTitle) {
            // Special case for title page.
            // No TinkerTexts, and image takes up a larger space.
            this.loadTitlePage(description);
        } else {
            // Load image.
            this.loadImage(description.storyImageFile);

            // Load all words as TinkerText. Start at beginning of a stanza.
            this.remainingStanzaWidth = 0;

            List<string> textWords =
                new List<string>(description.text.Split(' '));
            textWords.RemoveAll(String.IsNullOrEmpty);
            if (textWords.Count != description.timestamps.Length) {
                Logger.LogError("textWords doesn't match timestamps length " +
                                textWords.Count.ToString() + " " + 
                               description.timestamps.Length.ToString());
            }
            for (int i = 0; i < textWords.Count; i++)
            {
                // This will create the TinkerText and update stanzas.
                this.loadTinkerText(textWords[i], description.timestamps[i]);
            }
            // Set end timestamp of last stanza (edge case).
            this.stanzas[this.stanzas.Count - 1].GetComponent<Stanza>().SetEndTimestamp(
                this.tinkerTexts[this.tinkerTexts.Count - 1].GetComponent<TinkerText>().audioEndTime);
            // Load audio triggers for TinkerText.
            this.loadAudioTriggers();
        }

        // Load all scene objects.
        foreach (SceneObject sceneObject in description.sceneObjects) {
            this.loadSceneObject(sceneObject);
        }

        // Load triggers.
        foreach (Trigger trigger in description.triggers) {
            this.loadTrigger(trigger);
        }

        if (this.autoplayAudio) {
            this.audioManager.PlayAudio();
        }
    }

    // Begin playing the audio. Can be called by GameController in response
    // to UI events like button clicks or swipes.
    public void ToggleAudio() {
        this.audioManager.ToggleAudio();
    }

    private void loadTitlePage(SceneDescription description) {
        // Load the into the title panel without worrying about anything except
        // for fitting the space and making the aspect ratio correct.
        // Basically the same as first half of loadImage() function.
        string imageFile = description.storyImageFile;
        GameObject newObj = new GameObject();
        newObj.AddComponent<Image>();
        newObj.AddComponent<AspectRatioFitter>();
        newObj.transform.SetParent(this.titlePanel.transform, false);
        newObj.transform.localPosition = Vector3.zero;
        newObj.GetComponent<AspectRatioFitter>().aspectMode =
                  AspectRatioFitter.AspectMode.FitInParent;
        newObj.GetComponent<AspectRatioFitter>().aspectRatio =
                  this.titlePanelAspectRatio;
        Sprite sprite = Util.GetStorySprite(imageFile);
        newObj.GetComponent<Image>().sprite = sprite;
        newObj.GetComponent<Image>().preserveAspect = true;
        this.storyImage = newObj;
    }

    // Argument imageFile should be something like "the_hungry_toad_01" and then
    // this function will find it in the Resources directory and load it.
    private void loadImage(string imageFile) {
        string storyName = Util.FileNameToStoryName(imageFile);
        GameObject newObj = new GameObject();
        newObj.AddComponent<Image>();
        newObj.AddComponent<AspectRatioFitter>();
        newObj.GetComponent<AspectRatioFitter>().aspectMode =
          AspectRatioFitter.AspectMode.FitInParent;
        string fullImagePath = "StoryPages/" + storyName + "/" + imageFile;
        Sprite sprite = Resources.Load<Sprite>(fullImagePath);
        newObj.GetComponent<Image>().sprite = sprite;
        newObj.GetComponent<Image>().preserveAspect = true;
        newObj.transform.SetParent(this.graphicsPanel.transform, false);
        newObj.transform.localPosition = Vector3.zero;
        // Figure out sizing so that later scene objects can be loaded relative
        // to the background image for accurate overlay.
        Texture texture = Resources.Load<Texture>(fullImagePath);
        float imageAspectRatio = (float)texture.width / (float)texture.height;
        newObj.GetComponent<AspectRatioFitter>().aspectRatio =
          imageAspectRatio;
        // TODO: If height is constraining factor, then use up all possible
        // width by pushing the image over, only in landscape mode though.
        // Do the symmetric thing in portrait mode if width is constraining.
        if (imageAspectRatio > this.graphicsPanelAspectRatio) {
            // Width is the constraining factor.
            this.storyImageWidth = this.graphicsPanelWidth;
            this.storyImageHeight = this.graphicsPanelWidth / imageAspectRatio;
            this.storyImageX = 0;
            this.storyImageY = 
                -(this.graphicsPanelHeight - this.storyImageHeight) / 2;
        } else {
            // Height is the constraining factor.
            this.storyImageHeight = this.graphicsPanelHeight;
            this.storyImageWidth = this.graphicsPanelHeight * imageAspectRatio;
            if (this.displayMode == DisplayMode.Landscape) {
                float widthDiff = this.graphicsPanelWidth - this.storyImageWidth;
                this.graphicsPanelWidth = this.storyImageWidth;
                this.graphicsPanel.GetComponent<RectTransform>().sizeDelta =
                    new Vector2(this.storyImageWidth, this.storyImageHeight);
                Vector2 currentTextPanelSize =
                    this.textPanel.GetComponent<RectTransform>().sizeDelta;
                this.textPanel.GetComponent<RectTransform>().sizeDelta =
                    new Vector2(currentTextPanelSize.x + widthDiff,
                                currentTextPanelSize.y);   
            }
            this.storyImageY = 0;
            this.storyImageX = 
                (this.graphicsPanelWidth - this.storyImageWidth) / 2;
        }

        this.imageScaleFactor = this.storyImageWidth / texture.width;
        this.storyImage = newObj;
    }

    // Add a new TinkerText for the given word.
    private void loadTinkerText(string word, AudioTimestamp timestamp) {
        if (word.Length == 0) {
            return;
        }
		GameObject newTinkerText =
            Instantiate((GameObject)Resources.Load("Prefabs/TinkerText"));
        newTinkerText.GetComponent<TinkerText>()
             .Init(this.tinkerTexts.Count, word, timestamp);
        // Figure out how wide the TinkerText wants to be, then decide if
        // we need to make a new stanza.
        GameObject newText = newTinkerText.GetComponent<TinkerText>().text;
        float preferredWidth =
            LayoutUtility.GetPreferredWidth(
                newText.GetComponent<RectTransform>()
            );
        preferredWidth = Math.Max(preferredWidth, this.MIN_TINKER_TEXT_WIDTH);
        // Add new stanza if no more room, or if previous word was terminating
        // punctuation.
        if (preferredWidth > this.remainingStanzaWidth ||
            this.prevWordEndsStanza) {
            // Tell this tinkerText it's first in the stanza.
            newTinkerText.GetComponent<TinkerText>().SetFirstInStanza();
            GameObject newStanza =
                Instantiate((GameObject)Resources.Load("Prefabs/StanzaPanel"));
            newStanza.transform.SetParent(this.textPanel.transform, false);
            newStanza.GetComponent<Stanza>().Init(
                this.audioManager,
                this.textPanel.GetComponent<RectTransform>().position
            );
            // Set the end time of previous stanza and start time of the new
            // stanza we're adding.
            if (this.currentStanza != null) {
                this.currentStanza.GetComponent<Stanza>().SetEndTimestamp(
                timestamp.start);
            }
            this.stanzas.Add(newStanza);
            this.currentStanza = newStanza;
            this.currentStanza.GetComponent<Stanza>().SetStartTimestamp(
                timestamp.start);
            // Reset the remaining stanza width.
            this.remainingStanzaWidth =
                    this.textPanel.GetComponent<RectTransform>().sizeDelta.x;
        }
        // Initialize the TinkerText width correctly.
        // Set new TinkerText parent to be the stanza.
        newTinkerText.GetComponent<TinkerText>().SetWidth(preferredWidth);
		newTinkerText.transform.SetParent(this.currentStanza.transform, false);
        this.remainingStanzaWidth -= preferredWidth;
        this.remainingStanzaWidth -= STANZA_SPACING;
        this.tinkerTexts.Add(newTinkerText);
        this.prevWordEndsStanza = Util.WordShouldEndStanza(word);
    }

    // Adds a SceneObject to the story scene.
    private void loadSceneObject(SceneObject sceneObject) {
        // Allow multiple scene objects per label as long as they don't overlap.
        if (this.sceneObjectsLabelToId.ContainsKey(sceneObject.label)) {
            // Check for overlap.
            // TODO 
            if (Util.PositionsOverlap(
                    sceneObject.position,
                    this.sceneObjects[this.sceneObjectsLabelToId[sceneObject.label]]
                        .GetComponent<SceneObject>().position)) {
                return;
            }
        }
        GameObject newObj = 
            Instantiate((GameObject)Resources.Load("Prefabs/SceneObject"));
        newObj.transform.SetParent(this.graphicsPanel.transform, false);
        newObj.GetComponent<RectTransform>().SetAsLastSibling();
        // Set the position.
        SceneObjectManipulator manip =
            newObj.GetComponent<SceneObjectManipulator>();
        Position pos = sceneObject.position;
        manip.id = sceneObject.id;
        manip.label = sceneObject.label;
        Logger.Log("x, y " + storyImageX.ToString() + " " + storyImageY.ToString());
        manip.MoveToPosition(
            new Vector3(this.storyImageX + pos.left * this.imageScaleFactor,
                        this.storyImageY - pos.top * this.imageScaleFactor)
        )();
        manip.ChangeSize(
            new Vector2(pos.width * this.imageScaleFactor,
                        pos.height * this.imageScaleFactor)
        )();
        // Add a dummy handler to check things.
        manip.AddClickHandler(() =>
        {
            Logger.Log("SceneObject clicked " +
                       manip.label);
        });
        // TODO: if sceneObject.inText is false, set up whatever behavior we
        // want for these words.
        if (!sceneObject.inText) {
            manip.AddClickHandler(() =>
            {
                Logger.Log("Not in text! " + manip.label);
            });
        }
        // Name the GameObject so we can inspect in the editor.
        newObj.name = sceneObject.label;
        this.sceneObjects[sceneObject.id] = newObj;
    }

    // Sets up a trigger between TinkerTexts and SceneObjects.
    private void loadTrigger(Trigger trigger) {
        switch (trigger.type) {
            case TriggerType.CLICK_TINKERTEXT_SCENE_OBJECT:
                SceneObjectManipulator manip = 
                    this.sceneObjects[trigger.args.sceneObjectId]
                    .GetComponent<SceneObjectManipulator>();
                TinkerText tinkerText = this.tinkerTexts[trigger.args.textId]
                                            .GetComponent<TinkerText>();
                Action action = manip.Highlight(new Color(0, 1, 1, 60f / 255));
                tinkerText.AddClickHandler(action);
                break;
            default:
                Logger.LogError("Unknown TriggerType: " +
                                trigger.type.ToString());
                break;
                
        }
    }

    // Sets up a timestamp trigger on the audio manager.
    private void loadAudioTriggers() {
        foreach (GameObject t in this.tinkerTexts) {
            TinkerText tinkerText = t.GetComponent<TinkerText>();
            this.audioManager.AddTrigger(tinkerText.audioStartTime,
                                         tinkerText.OnStartAudioTrigger,
                                         tinkerText.isFirstInStanza);
            this.audioManager.AddTrigger(
                tinkerText.audioEndTime, tinkerText.OnEndAudioTrigger); 
        }
    }

    // Called by GameController to change whether we autoplay o not.
    public void SetAutoplay(bool newValue) {
        this.autoplayAudio = newValue;
    } 

    // Called by GameController when we should remove all elements we've added
    // to this page (usually in preparration for the creation of another page).
    public void ClearPage() {
        // Destroy stanzas.
        foreach (GameObject stanza in this.stanzas) {
            Destroy(stanza);
        }
        this.stanzas.Clear();
        // Destroy TinkerText objects we have a reference to, and reset list.
        foreach (GameObject tinkertext in this.tinkerTexts) {
            Destroy(tinkertext);
        }
        this.tinkerTexts.Clear();
        // Destroy SceneObjects we have a reference to, and empty dictionary.
        foreach (KeyValuePair<int,GameObject> obj in this.sceneObjects) {
            Destroy(obj.Value);
        }
        this.sceneObjects.Clear();
        // Remove all images.
        Destroy(this.storyImage.gameObject);
        this.storyImage = null;
        // Remove audio triggers.
        this.audioManager.ClearTriggersAndReset();

        this.prevWordEndsStanza = false;
    }

    // Update the display mode. We need to update our internal references to
    // textPanel and graphicsPanel.
    private void setDisplayMode(DisplayMode newMode) {
        if (this.displayMode != newMode) {
            this.displayMode = newMode;
            if (this.graphicsPanel != null) {
                this.graphicsPanel.SetActive(false);
                this.textPanel.SetActive(false);
                this.titlePanel.SetActive(false);
            }
            switch (this.displayMode)
            {
                case DisplayMode.Landscape:
                    this.graphicsPanel = this.landscapeGraphicsPanel;
                    this.textPanel = this.landscapeTextPanel;
                    this.titlePanel = this.landscapeTitlePanel;
                    break;
                case DisplayMode.LandscapeWide:
                    this.graphicsPanel = this.landscapeWideGraphicsPanel;
                    this.textPanel = this.landscapeWideTextPanel;
                    this.titlePanel = this.landscapeTitlePanel;
                    break;
                case DisplayMode.Portrait:
                    this.graphicsPanel = this.portraitGraphicsPanel;
                    this.textPanel = this.portraitTextPanel;
                    this.titlePanel = this.portraitTitlePanel;
                    // Resize back to normal.
                    this.graphicsPanel.GetComponent<RectTransform>().sizeDelta =
                            new Vector2(this.PORTRAIT_GRAPHICS_WIDTH,
                                        this.MAX_PORTRAIT_GRAPHICS_HEIGHT);
                    break;
                default:
                    Logger.LogError("unknown display mode " + newMode);
                    break;
            }
            this.graphicsPanel.SetActive(true);
            this.textPanel.SetActive(true);
            this.titlePanel.SetActive(true);
            Vector2 rect =
                this.graphicsPanel.GetComponent<RectTransform>().sizeDelta;
            this.graphicsPanelWidth = (float)rect.x;
            this.graphicsPanelHeight = (float)rect.y;
            this.graphicsPanelAspectRatio =
                this.graphicsPanelWidth / this.graphicsPanelHeight;
            rect = this.titlePanel.GetComponent<RectTransform>().sizeDelta;
            this.titlePanelAspectRatio = (float)rect.x / (float)rect.y;
        }

    }

    private void resetPanelSizes() {
        switch(this.displayMode) {
            case DisplayMode.Landscape:
                this.graphicsPanel.GetComponent<RectTransform>().sizeDelta =
                    new Vector2(this.MAX_LANDSCAPE_GRAPHICS_WIDTH,
                                this.LANDSCAPE_GRAPHICS_HEIGHT);
                this.textPanel.GetComponent<RectTransform>().sizeDelta =
                        new Vector2(LANDSCAPE_TEXT_WIDTH,
                                    this.textPanel.GetComponent<RectTransform>().sizeDelta.y);
                break;
            case DisplayMode.Portrait:
                this.graphicsPanel.GetComponent<RectTransform>().sizeDelta =
                    new Vector2(this.PORTRAIT_GRAPHICS_WIDTH,
                                this.MAX_PORTRAIT_GRAPHICS_HEIGHT);
                this.textPanel.GetComponent<RectTransform>().sizeDelta =
                        new Vector2(this.textPanel.GetComponent<RectTransform>().sizeDelta.x,
                                    this.PORTRAIT_TEXT_HEIGHT);
                break;
            case DisplayMode.LandscapeWide:
                break;
            default:
                break;
        }
    }

}
