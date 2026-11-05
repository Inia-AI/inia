import pluginVue from 'eslint-plugin-vue'
import vueTsEslintConfig from '@vue/eslint-config-typescript'
import skipFormatting from '@vue/eslint-config-prettier/skip-formatting'
import pluginVueA11y from 'eslint-plugin-vuejs-accessibility'

const a11yRecommendedRules = Object.assign({}, ...pluginVueA11y.configs['flat/recommended'].map(config => config.rules ?? {}))

export default [
  {
    name: 'app/files-to-lint',
    files: ['**/*.{ts,mts,tsx,vue}'],
  },

  {
    name: 'app/files-to-ignore',
    ignores: ['**/dist/**', '**/dist-ssr/**', '**/coverage/**', '.claude/**', '.worktree/**', 'chart-builder/**'],
  },

  ...pluginVue.configs['flat/essential'],
  ...vueTsEslintConfig(),
  skipFormatting,

  {
    rules: {
      '@typescript-eslint/no-explicit-any': 'warn',
    },
  },

  {
    name: 'app/accessibility',
    files: ['**/*.vue'],
    plugins: { 'vuejs-accessibility': pluginVueA11y },
    rules: {
      ...a11yRecommendedRules,
      'vuejs-accessibility/form-control-has-label': [
        'error',
        {
          labelComponents: ['FormControl'],
          controlComponents: [
            'inia-select',
            'searchable-select',
            'multi-select',
            'counterparty-searchable-select',
            'peer-company-multi-select',
            'baseline-report-select',
          ],
        },
      ],
      'vuejs-accessibility/label-has-for': [
        'error',
        { required: { some: ['nesting', 'id'] }, controlComponents: ['Checkbox', 'Switch', 'RadioGroupItem'] },
      ],
      'vuejs-accessibility/no-aria-hidden-on-focusable': 'error',
      'vuejs-accessibility/no-role-presentation-on-focusable': 'error',
      'vue/html-button-has-type': 'error',
      'vue/no-restricted-syntax': [
        'error',
        {
          selector:
            "VElement:not([rawName=/^(button|a|Button|RouterLink|CommandItem|DropdownMenuItem|DropdownMenuTrigger|PopoverTrigger|SelectItem|SelectTrigger|TabsTrigger|AccordionTrigger|Checkbox|Switch)$/]) > VStartTag:has(VAttribute[directive=true][key.name.name='on'][key.argument.name=/^(click|dblclick|mousedown|pointerdown)$/][value], VAttribute[directive=true][key.name.name='on']:not([key.argument]) Property[key.name=/^(click|dblclick|mousedown|pointerdown)$/]):not(:has(VAttribute[key.name='as'][value.value='button'])):not(:has(VAttribute[directive=true][key.name.name='on'][key.argument.name=/^key/]):has(VAttribute[key.name='tabindex']:not([value.value='-1']), VAttribute[directive=true][key.name.name='bind'][key.argument.name='tabindex']:not([value.expression.operator='-'])))",
          message:
            'A click handler here has no keyboard path. Use a native <button type="button">, a link, or a focusable element with a key handler.',
        },
        {
          selector:
            "VElement[rawName=/^(DataTable|data-table)$/] > VStartTag > VAttribute[directive=true][key.name.name='on'][key.argument.name=/^row-?click$/i]",
          message: 'DataTable rows open a page through rowTo, which gives each row a link. Pass :row-to instead of listening to row-click.',
        },
      ],
    },
  },

  {
    name: 'app/sanitised-html',
    files: ['**/*.vue'],
    rules: {
      'vue/no-v-html': 'error',
      'vue/no-restricted-v-bind': [
        'error',
        { argument: '/^(innerHTML|inner-html|outerHTML|outer-html|srcdoc)$/', message: 'Put HTML on the page through v-safe-html.' },
      ],
    },
  },

  {
    name: 'app/sanitised-html-script',
    files: ['src/**/*.{ts,vue}'],
    ignores: ['**/*.test.ts', 'src/test/**', 'src/lib/safeHtml.ts'],
    rules: {
      'no-restricted-syntax': [
        'error',
        ...['name', 'value'].map(key => ({
          selector: `AssignmentExpression[left.property.${key}=/^(innerHTML|outerHTML)$/]:not([right.callee.name=/^(sanitizeHtml|sanitizeModelHtml|renderContent)$/]):not([right.value=''])`,
          message: 'Put HTML on the page through v-safe-html, or pass it through sanitizeHtml, sanitizeModelHtml or renderContent in the same statement.',
        })),
        {
          selector:
            "Property[key.name=/^(innerHTML|outerHTML)$/]:not([value.callee.name=/^(sanitizeHtml|sanitizeModelHtml|renderContent)$/])",
          message: 'Put HTML on the page through v-safe-html, or pass it through sanitizeHtml, sanitizeModelHtml or renderContent in the same statement.',
        },
        {
          selector:
            "CallExpression[callee.property.name='insertAdjacentHTML'], CallExpression[callee.object.name='document'][callee.property.name=/^(write|writeln)$/]",
          message: 'Put HTML on the page through v-safe-html, or set innerHTML from sanitizeHtml, sanitizeModelHtml or renderContent.',
        },
      ],
    },
  },

  // Relax rules for auto-generated UI components from reka-ui
  {
    name: 'app/ui-components-overrides',
    files: ['src/components/ui/**/*.vue'],
    rules: {
      'vue/multi-word-component-names': 'off',
      '@typescript-eslint/no-unused-vars': 'off',
    },
  },
]
