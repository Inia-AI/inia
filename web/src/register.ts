import { library } from '@fortawesome/fontawesome-svg-core'
import type { IconDefinition } from '@fortawesome/fontawesome-svg-core'
import { FontAwesomeIcon } from '@fortawesome/vue-fontawesome'
import type { App, Component } from 'vue'
import * as icons from './icons'

import * as Atoms from '@/components/atoms'
import * as Organisms from '@/components/organisms'

export default function (app: App) {
  const iconMap = icons as Record<string, IconDefinition>
  Object.keys(iconMap).forEach(x => {
    library.add(iconMap[x])
  })

  app.component('font-awesome-icon', FontAwesomeIcon)

  /* Register common components */
  const atomsMap = Atoms as Record<string, Component>
  Object.keys(atomsMap).forEach(x => {
    app.component(x, atomsMap[x])
  })

  const organismsMap = Organisms as Record<string, Component>
  Object.keys(organismsMap).forEach(x => {
    app.component(x, organismsMap[x])
  })
}
