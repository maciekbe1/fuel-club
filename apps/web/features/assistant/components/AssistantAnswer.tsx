import remarkBreaks from "remark-breaks";
import { defaultRemarkPlugins } from "streamdown";
import { MessageResponse } from "@/components/ai-elements/message";

const REMARK_PLUGINS = [...Object.values(defaultRemarkPlugins), remarkBreaks];

/**
 * The model's answer as markdown. No images and no links (shown as plain text): a reply must not
 * make the browser fetch or open a third-party URL. Single line breaks are kept; streamdown's
 * built-in controls (English labels) are off.
 */
export function AssistantAnswer({ text }: { text: string }) {
  return (
    <MessageResponse
      remarkPlugins={REMARK_PLUGINS}
      disallowedElements={["img", "a"]}
      unwrapDisallowed
      controls={false}
    >
      {text}
    </MessageResponse>
  );
}
