using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;

namespace ICE.Ui;

public class StyledImageButton
{
    public static bool DrawStyledImageButton(IDalamudTextureWrap? icon, Vector2 size, bool enabled = true)
    {
        Vector4 buttonColor, borderColor;
        float borderSize;

        if (enabled)
        {
            buttonColor = new Vector4(0.3f, 0.3f, 0.35f, 0.7f);
            borderColor = new Vector4(0.898f, 0.8f, 0.501f, 1f);
            borderSize = 1.0f;
        }
        else
        {
            buttonColor = new Vector4(0.2f, 0.2f, 0.2f, 0.1f);
            borderColor = new Vector4(0.4f, 0.4f, 0.4f, 0.5f);
            borderSize = 0.5f;
        }

        // Apply the custom styling
        ImGui.PushStyleColor(ImGuiCol.Button, buttonColor);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, buttonColor * 1.1f); // Slightly brighter on hover
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, buttonColor * 0.9f);  // Slightly darker when pressed
        ImGui.PushStyleColor(ImGuiCol.Border, borderColor);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, borderSize);

        // Create the ImageButton
        bool clicked = ImGui.ImageButton(icon.Handle, size);

        // Restore original styling
        ImGui.PopStyleVar(); // FrameBorderSize
        ImGui.PopStyleColor(4); // Button, ButtonHovered, ButtonActive, Border

        return clicked;
    }

    public static bool DrawStyledImageButton(ISharedImmediateTexture? icon, Vector2 size, bool enabled = true)
    {
        Vector4 buttonColor, borderColor;
        float borderSize;

        if (enabled)
        {
            buttonColor = new Vector4(0.3f, 0.3f, 0.35f, 0.7f);
            borderColor = new Vector4(0.898f, 0.8f, 0.501f, 1f);
            borderSize = 1.0f;
        }
        else
        {
            buttonColor = new Vector4(0.2f, 0.2f, 0.2f, 0.1f);
            borderColor = new Vector4(0.4f, 0.4f, 0.4f, 0.5f);
            borderSize = 0.5f;
        }

        float zoomFactor = 0.25f; // 25% zoom-in
        float cropAmount = zoomFactor / 2; // Crop equally from all sides

        Vector2 uv0 = enabled ? new Vector2(0, 0) : new Vector2(cropAmount, cropAmount);
        Vector2 uv1 = enabled ? new Vector2(1, 1) : new Vector2(1 - cropAmount, 1 - cropAmount);

        // Applies the custom code
        ImGui.PushStyleColor(ImGuiCol.Button, buttonColor);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, buttonColor * 1.1f); // Slightly brighter on hover
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, buttonColor * 0.9f);  // Slightly darker when pressed
        ImGui.PushStyleColor(ImGuiCol.Border, borderColor);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, borderSize);

        bool clicked = ImGui.ImageButton(icon.GetWrapOrEmpty().Handle, size, uv0, uv1);

        // Restore original styling
        ImGui.PopStyleVar(); // FrameBorderSize
        ImGui.PopStyleColor(4); // Button, ButtonHovered, ButtonActive, Border

        return clicked;
    }
}
