import { v } from 'convex/values';
import { Id } from '../_generated/dataModel';
import { ActionCtx, internalQuery } from '../_generated/server';
import { LLMMessage, chatCompletion } from '../util/llm';
import * as memory from './memory';
import { api, internal } from '../_generated/api';
import * as embeddingsCache from './embeddingsCache';
import { GameId, conversationId, playerId } from '../aiTown/ids';
import { NUM_MEMORIES_TO_SEARCH } from '../constants';

const selfInternal = internal.agent.conversation;

let conversationHistory = []; // 全局变量用于存储对话历史

async function generateResponse(ctx, playerName, otherPlayerName, userDialogue, userEmotion) {
  const prompt = [
    `You are ${otherPlayerName}, and ${playerName} just said: "${userDialogue}" with a ${userEmotion} expression.`,
    `Respond to ${playerName} appropriately.`,
  ];
  
  const { content: dialogue } = await chatCompletion({
    messages: [
      {
        role: 'user',
        content: prompt.join('\n'),
      },
    ],
    max_tokens: 300,
    stream: true,
    stop: stopWords(playerName, otherPlayerName),
  });

  const { content: emotion } = await chatCompletion({
    messages: [
      {
        role: 'user',
        content: `Analyze the following dialogue and determine the emotion: "${dialogue}"`,
      },
    ],
    max_tokens: 10,
    stream: true,
  });

  return { dialogue, emotion };
}

async function continueConversation(ctx, playerName, userDialogue, userEmotion) {
  const dadName = 'Dad';
  const momName = 'Mom';
  const sisterName = 'Sister';

  const dadResponse = await generateResponse(ctx, playerName, dadName, userDialogue, userEmotion);
  const momResponse = await generateResponse(ctx, playerName, momName, userDialogue, userEmotion);
  const sisterResponse = await generateResponse(ctx, playerName, sisterName, userDialogue, userEmotion);

  conversationHistory.push({ speaker: dadName, dialogue: dadResponse.dialogue, emotion: dadResponse.emotion });
  conversationHistory.push({ speaker: momName, dialogue: momResponse.dialogue, emotion: momResponse.emotion });
  conversationHistory.push({ speaker: sisterName, dialogue: sisterResponse.dialogue, emotion: sisterResponse.emotion });

  return {
    dad: dadResponse,
    mom: momResponse,
    sister: sisterResponse,
  };
}

export async function familyGatheringResponse(
  ctx: ActionCtx,
  initialDialogue: string,
  initialEmotion: string,
  playerResponse: string = null,
  playerEmotion: string = null
) {
  const playerName = 'You';

  // 初始对话
  if (conversationHistory.length === 0) {
    const initialResponses = await continueConversation(ctx, playerName, initialDialogue, initialEmotion);
    conversationHistory.push({ speaker: playerName, dialogue: initialDialogue, emotion: initialEmotion });
    return initialResponses;
  }

  // 后续对话
  if (playerResponse && playerEmotion) {
    const playerDialogue = playerResponse;
    const playerEmotionAnalysis = playerEmotion;
    conversationHistory.push({ speaker: playerName, dialogue: playerDialogue, emotion: playerEmotionAnalysis });

    const subsequentResponses = await continueConversation(ctx, playerName, playerDialogue, playerEmotionAnalysis);
    return subsequentResponses;
  }

  // 自动生成“我”的响应
  const lastDialogue = conversationHistory[conversationHistory.length - 1];
  const playerDialogue = lastDialogue.dialogue;
  const embedding = await embeddingsCache.fetch(ctx, `Respond to: ${playerDialogue}`);

  const memories = await memory.searchMemories(ctx, playerName, embedding, NUM_MEMORIES_TO_SEARCH);
  const memoryPrompts = memories.map(mem => `Memory: ${mem.description}`).join('\n');

  const autoPrompt = [
    `You are ${playerName}.`,
    `Below is the conversation history:`,
    ...conversationHistory.map(hist => `${hist.speaker}: ${hist.dialogue}`),
    `Now respond appropriately.`,
    memoryPrompts,
  ];

  const { content: autoResponse } = await chatCompletion({
    messages: [
      {
        role: 'user',
        content: autoPrompt.join('\n'),
      },
    ],
    max_tokens: 300,
    stream: true,
  });

  const { content: autoEmotion } = await chatCompletion({
    messages: [
      {
        role: 'user',
        content: `Analyze the following dialogue and determine the emotion: "${autoResponse}"`,
      },
    ],
    max_tokens: 10,
    stream: true,
  });

  conversationHistory.push({ speaker: playerName, dialogue: autoResponse, emotion: autoEmotion });

  const subsequentResponses = await continueConversation(ctx, playerName, autoResponse, autoEmotion);
  return subsequentResponses;
}

function stopWords(playerName, otherPlayerName) {
  const variants = [`${otherPlayerName} to ${playerName}`];
  return variants.flatMap((stop) => [stop + ':', stop.toLowerCase() + ':']);
}
