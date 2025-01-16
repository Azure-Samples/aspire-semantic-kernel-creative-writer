// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

import { Button } from "@fluentui/react-components";
import {
  AIChatMessage,
  AIChatCompletionDelta,
  AIChatMessageDelta,
  AIChatProtocolClient,
  AIChatError,
} from "@microsoft/ai-chat-protocol";
import { useEffect, useId, useRef, useState } from "react";
import ReactMarkdown from "react-markdown";
import TextareaAutosize from "react-textarea-autosize";
import styles from "./Chat.module.css";
import gfm from "remark-gfm";

type ChatEntry = AIChatMessage | AIChatError;

function isChatError(entry: unknown): entry is AIChatError {
  return (entry as AIChatError).code !== undefined;
}

interface AIAgentChatMessage extends AIChatMessage {
  context?: {
    name: string;
  };
}

interface AIAgentChatMessageDelta extends AIChatMessageDelta {
  context?: {
    name: string;
  };
}

interface AIAgentChatCompletionDelta extends AIChatCompletionDelta {
  delta: AIAgentChatMessageDelta;
}

export default function Chat({ style }: { style: React.CSSProperties }) {
  const client = new AIChatProtocolClient("/api/chat");

  const [messages, setMessages] = useState<ChatEntry[]>([]);
  const [input, setInput] = useState<string>("");
  const [sending, setSending] = useState<boolean>(false);
  const inputId = useId();
  const [sessionState, setSessionState] = useState<unknown>(undefined);
  const messagesEndRef = useRef<HTMLDivElement>(null);

  const scrollToBottom = () => {
    messagesEndRef.current?.scrollIntoView({ behavior: "smooth" });
  };
  useEffect(scrollToBottom, [messages]);

  const sendMessage = async () => {
    setSending(true);
    const message: AIChatMessage = {
      role: "user",
      content: input,
    };
    var updatedMessages = [...messages, message];
    setMessages(updatedMessages);

    setInput("");
    try {
      const result = await client.getStreamedCompletion([message], {
        sessionState: sessionState,
      });

      let latestMessage: AIAgentChatMessage = {
        content: "",
        role: "assistant",
        context: { name: "dummy" },
      };
      for await (const response of result) {
        debugger;
        const agentResponse = response as AIAgentChatCompletionDelta;
        if (agentResponse.sessionState) {
          setSessionState(agentResponse.sessionState);
        }
        if (!agentResponse.delta) {
          continue;
        }
        if (agentResponse.delta.role) {
          const chatRole = agentResponse.delta.role;
          const agentName = agentResponse.delta.context?.name;
          const latestAgentName = latestMessage.context?.name;

          // If the role changes or the agent changes, we create a new message
          if (chatRole != latestMessage.role || agentName != latestAgentName) {
            // If there was a message before, we need to save it
            if (latestMessage.context?.name != "dummy") {
              updatedMessages = [...updatedMessages, latestMessage];
            }
            latestMessage = {
              content: agentName ? `**${agentName} Agent:**  \n` : "",
              role: chatRole,
              context: agentResponse.delta.context,
            };
          }
        }
        if (agentResponse.delta.content) {
          latestMessage.content += agentResponse.delta.content;
          setMessages([...updatedMessages, latestMessage]);
        }
      }
    } catch (e) {
      if (isChatError(e)) {
        setMessages([...updatedMessages, e]);
      }
    } finally {
      setSending(false);
    }
  };

  const getClassName = (message: ChatEntry) => {
    if (isChatError(message)) {
      return styles.caution;
    }

    if (message.role === "system") {
      return styles.systemMessage;
    }

    return message.role === "user"
      ? styles.userMessage
      : styles.assistantMessage;
  };

  const getErrorMessage = (message: AIChatError) => {
    return `${message.code}: ${message.message}`;
  };

  return (
    <div className={styles.chatWindow} style={style}>
      <div className={styles.messages}>
        {messages.map((message) => (
          <div key={crypto.randomUUID()} className={getClassName(message)}>
            {isChatError(message) ? (
              <>{getErrorMessage(message)}</>
            ) : (
              <div className={styles.messageBubble}>
                <ReactMarkdown remarkPlugins={[gfm]}>
                  {message.content}
                </ReactMarkdown>
              </div>
            )}
          </div>
        ))}
        <div ref={messagesEndRef} />
      </div>
      <div className={styles.inputArea}>
        <TextareaAutosize
          id={inputId}
          value={input}
          disabled={sending}
          onChange={(e) => setInput(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === "Enter" && e.shiftKey) {
              e.preventDefault();
              sendMessage();
            }
          }}
          minRows={1}
          maxRows={4}
        />
        <Button onClick={sendMessage}>Send</Button>
      </div>
    </div>
  );
}
