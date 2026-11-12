// What makes rendered markup unsafe to show: an element that runs, loads or submits, a handler
// attribute, a script-bearing address, or a style that loads from elsewhere. The checks read
// parsed elements, not the string.

const RUNNING_ELEMENTS = ['script', 'iframe', 'object', 'embed', 'link', 'meta', 'base', 'style', 'form', 'svg', 'math']
const ADDRESS_ATTRIBUTES = ['href', 'src', 'xlink:href', 'action', 'formaction', 'srcdoc', 'data', 'poster', 'background', 'ping', 'srcset']
const SCRIPT_ADDRESSES = ['javascript:', 'vbscript:', 'data:text/html']

export const parseMarkup = (html: string): Element[] => [
  ...new DOMParser().parseFromString(`<body>${html}</body>`, 'text/html').body.querySelectorAll('*'),
]

export const markupProblems = (elements: Element[], alsoForbidden: string[] = []): string[] =>
  elements.flatMap(element => {
    const problems: string[] = []
    if (RUNNING_ELEMENTS.includes(element.localName) || alsoForbidden.includes(element.localName)) {
      problems.push(`<${element.localName}> element`)
    }
    for (const attribute of [...element.attributes]) {
      const name = attribute.name.toLowerCase()
      const value = attribute.value.trim().toLowerCase()
      if (name.startsWith('on')) {
        problems.push(`${name} on <${element.localName}>`)
      }
      if (ADDRESS_ATTRIBUTES.includes(name) && SCRIPT_ADDRESSES.some(scheme => value.startsWith(scheme))) {
        problems.push(`${name}="${attribute.value}" on <${element.localName}>`)
      }
      if (name === 'style' && (value.includes('url(') || value.includes('image-set(') || value.includes('\\'))) {
        problems.push(`style "${attribute.value}" on <${element.localName}>`)
      }
    }
    return problems
  })
