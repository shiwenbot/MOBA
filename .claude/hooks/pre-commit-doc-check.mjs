#!/usr/bin/env node

/**
 * Pre-commit 文档进度同步检查
 *
 * 在 git commit 前触发，检测本次提交是否涉及阶段性完成，
 * 如果 Plan/ 阶段总目录未同步更新，则阻止提交并提示修复。
 */

import { execSync } from 'child_process';
import { join } from 'path';

const root = execSync('git rev-parse --show-toplevel', { encoding: 'utf8' }).trim();

let data = '';
process.stdin.on('data', c => (data += c));
process.stdin.on('end', () => {
  try {
    const { command = '' } = JSON.parse(data || '{}');

    // 只拦截 git commit 命令
    if (!/\bgit\s+commit\b/.test(command)) process.exit(0);

    // 获取暂存区文件列表
    const staged = execSync('git diff --staged --name-only', {
      encoding: 'utf8',
      cwd: root,
    })
      .trim()
      .split('\n')
      .filter(Boolean);

    const codeFiles = staged.filter(f => /\.(cs|ts|js)$/.test(f));
    const planFiles = staged.filter(f => f.startsWith('Plan/'));

    // 没有代码变更则跳过
    if (!codeFiles.length) process.exit(0);

    // 从提交命令中提取版本标签（v0.5a, MVP-c, v0.3-arch 等）
    const tags = [
      ...new Set(
        (command.match(/\b(v\d+\.\d+(?:-\w+)?[a-z]?|MVP-[a-z])\b/gi) || [])
      ),
    ];

    // 定义模块 → 阶段总目录映射
    const docMaps = [
      {
        name: '状态帧同步',
        path: 'Plan/状态帧同步/状态帧同步-阶段总目录.md',
        dirs: ['Battle', 'FrameSync'],
      },
      {
        name: '战斗Buff系统',
        path: 'Plan/战斗Buff系统/战斗Buff系统-阶段总目录.md',
        dirs: ['Buff'],
      },
      {
        name: '技能节点编辑器',
        path: 'Plan/技能节点编辑器/',
        dirs: ['SkillGraph'],
      },
    ];

    // 找出有代码变更但文档未更新的模块
    const issues = [];
    for (const doc of docMaps) {
      const hasCode = codeFiles.some(f => doc.dirs.some(d => f.includes(d)));
      const hasPlan = planFiles.some(f => f.startsWith(doc.path));
      if (hasCode && !hasPlan) {
        issues.push(doc);
      }
    }

    if (!issues.length) process.exit(0);

    if (tags.length) {
      // 有版本标签 → 强烈信号，阻止提交
      const lines = issues.map(d => `  - ${d.name}: ${d.path}`);
      console.error(
        [
          '',
          '🚫 文档进度未同步！',
          `提交包含版本标签 [${tags.join(', ')}]，但以下阶段总目录未更新：`,
          ...lines,
          '',
          '请先更新文档中的阶段状态标记，然后再重新提交。',
          '',
        ].join('\n')
      );
      process.exit(2);
    } else {
      // 无版本标签 → 弱信号，仅提醒
      console.error(
        [
          '',
          '💡 提醒：本次代码变更涉及以下模块，但未更新 Plan/ 文档：',
          ...issues.map(d => `  - ${d.name} (${d.path})`),
          '如为阶段性完成，请确认进度文档是否需要同步更新。',
          '',
        ].join('\n')
      );
      process.exit(0);
    }
  } catch {
    // 出错时不阻止提交
    process.exit(0);
  }
});
