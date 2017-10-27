// This file contains constants for the interactive storybook.

public static class Constants {
    // ROS connection.
    public static bool USE_ROS = true;
    public static string DEFAULT_ROSBRIDGE_IP = "192.168.1.149";
    public static string DEFAULT_ROSBRIDGE_PORT = "9090";

    // ROS topics.


}

// Orientations.
public enum Orientation {
    Portrait,
    Landscape
}

// Display Modes.
// Related to Orientation but also deals with layout of the scene.
public enum DisplayMode {
    LandscapeWide,
    Landscape,
    Portrait
};
