<template>
  <div class="layout-container">
    <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions, vue/no-restricted-syntax -- backdrop only: Escape closes the sidebar -->
    <div
      :class="sidebarOpen ? 'block' : 'hidden'"
      @click="sidebarOpen = false"
      class="mobile-overlay"
    ></div>

    <div
      ref="sidebar"
      :class="{ 'mobile-sidebar--open': sidebarOpen }"
      class="mobile-sidebar"
      role="dialog"
      aria-modal="true"
      :aria-label="$t('shared.layout.navigation')"
      :inert="!sidebarOpen || undefined"
    >
      <div class="sidebar-content">
        <button
          type="button"
          class="button-reset sidebar-logo"
          :aria-label="$t('shared.layout.home')"
          @click="onLogoClick"
        >
          <IniaWordmark class="logo-image" />
        </button>

        <nav class="sidebar-nav">
          <RouterLink
            v-for="(item, idx) in menuItems"
            :key="idx"
            class="nav-link"
            activeClass="nav-link-active"
            :to="item.to"
            @click="sidebarOpen = false"
          >
            <span class="nav-text">{{ item.title }}</span>
          </RouterLink>
        </nav>
      </div>
    </div>

    <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions, vue/no-restricted-syntax -- backdrop only: Escape and the "Close tools menu" button close the drawer -->
    <div
      v-if="drawerOpen"
      class="drawer-overlay"
      @click="drawerOpen = false"
    ></div>

    <div
      ref="drawer"
      class="desktop-drawer"
      :class="{ 'drawer-open': drawerOpen }"
      role="dialog"
      aria-modal="true"
      :aria-labelledby="drawerTitleId"
      :inert="!drawerOpen || undefined"
    >
      <div class="drawer-header">
        <span :id="drawerTitleId" class="drawer-title">{{ $t('shared.layout.tools') }}</span>
        <button type="button" class="drawer-close" :aria-label="$t('shared.layout.closeTools')" @click="drawerOpen = false">
          <IniaIcon name="xmark" />
        </button>
      </div>
      <nav class="drawer-nav">
        <RouterLink
          v-for="(item, idx) in hamburgerMenuItems"
          :key="idx"
          class="drawer-link"
          activeClass="drawer-link-active"
          :to="item.to"
          @click="drawerOpen = false"
        >
          <IniaIcon :name="item.icon" class="drawer-link-icon" />
          {{ item.title }}
        </RouterLink>
      </nav>
    </div>

    <!-- Main Content Area -->
    <div class="main-container" :inert="drawerOpen || sidebarOpen || undefined">
      <!-- Header -->
      <header ref="header" class="header">
        <div ref="headerStart" class="header-left">
          <button
            v-if="!menuFits || hamburgerMenuItems.length"
            ref="panelTrigger"
            type="button"
            class="hamburger-trigger"
            :aria-label="menuFits ? $t('shared.layout.openTools') : $t('shared.layout.openNavigation')"
            :aria-expanded="menuFits ? drawerOpen : sidebarOpen"
            @click="openPanel"
          >
            <IniaIcon name="bars" class="hamburger-icon" />
          </button>
          <button
            type="button"
            class="button-reset header-logo"
            :aria-label="$t('shared.layout.home')"
            @click="onLogoClick"
          >
            <IniaWordmark class="logo-image" />
          </button>
        </div>

        <div class="desktop-nav" :class="{ 'desktop-nav--collapsed': !menuFits }" :inert="!menuFits || undefined">
          <div ref="headerMenu" class="desktop-nav-items">
            <template v-for="(item, idx) in mainMenuItems" :key="idx">
              <DropdownMenu v-if="item.children">
                <DropdownMenuTrigger as-child>
                  <button
                    type="button"
                    class="desktop-nav-link"
                    :class="{ 'desktop-nav-link-active': isGroupActive(item.pathPrefix) }"
                  >
                    <span class="desktop-nav-text">{{ item.title }}</span>
                    <IniaIcon name="chevron-down" class="nav-dropdown-icon" />
                  </button>
                </DropdownMenuTrigger>
                <DropdownMenuContent align="start">
                  <DropdownMenuItem
                    v-for="(child, cIdx) in item.children"
                    :key="cIdx"
                    class="cursor-pointer"
                    @click="router.push(child.to)"
                  >
                    {{ child.title }}
                  </DropdownMenuItem>
                </DropdownMenuContent>
              </DropdownMenu>

              <RouterLink
                v-else
                class="desktop-nav-link"
                activeClass="desktop-nav-link-active"
                :to="item.to"
              >
                <span class="desktop-nav-text">{{ item.title }}</span>
                <span
                  v-if="item.to === '/signatures' && signatureReviewCount > 0"
                  class="desktop-nav-review-badge"
                  :title="$t('page.signatures.editor.needsReviewTooltip', { count: signatureReviewCount }, signatureReviewCount)"
                >
                  {{ signatureReviewCount }}
                </span>
                <span
                  v-if="item.to === '/signatures' && signatureProposerActionCount > 0"
                  class="desktop-nav-review-badge desktop-nav-review-badge--proposer"
                  :title="$t('page.signatures.editor.needsProposerActionTooltip', { count: signatureProposerActionCount }, signatureProposerActionCount)"
                >
                  {{ signatureProposerActionCount }}
                </span>
              </RouterLink>
            </template>
          </div>
        </div>

        <!-- Header Actions -->
        <div ref="headerEnd" class="header-actions">
          <div class="tenant-selector" v-if="selectedTenant && showTenantSelector">
            <IniaSelect
              v-model="selectedTenant"
              :options="tenantOptions"
              :aria-label="$t('shared.layout.organization')"
              @update:modelValue="onTenantChange"
            />
          </div>

          <ThemeToggle />

          <button
            v-if="isOperator"
            type="button"
            class="button-reset download-log-btn"
            :aria-label="$t('shared.layout.downloadLog')"
            @click="onDownloadLog"
          >
            <IniaIcon name="download" class="download-icon" />
            <span class="download-text">LOG</span>
          </button>

          <DropdownMenu>
            <DropdownMenuTrigger :aria-label="$t('shared.layout.accountMenu')">
              <div class="user-menu-trigger">
                <IniaIcon name="user" class="user-icon" />
              </div>
            </DropdownMenuTrigger>
            <DropdownMenuContent class="user-menu-content">
              <DropdownMenuItem class="cursor-pointer" @click="onProfile">{{
                $t('shared.layout.profile')
              }}</DropdownMenuItem>
              <component :is="item" v-for="(item, index) in extensionPoints.userMenuItems" :key="index" />
              <DropdownMenuItem class="cursor-pointer" @click="onLogout">{{
                $t('shared.layout.logout')
              }}</DropdownMenuItem>
            </DropdownMenuContent>
          </DropdownMenu>
        </div>
      </header>

      <!-- Main Content -->
      <main class="main-content">
        <slot />
        <component :is="overlay" v-for="(overlay, index) in extensionPoints.overlays" :key="index" />
        <div v-if="FE_VERSION || backendVersion?.version" class="version-info">
          <div class="version-item" v-if="backendVersion?.version">
            <p>Backend Version:</p>
            <p>{{ backendVersion?.version }}</p>
          </div>
          <div class="version-item" v-if="FE_VERSION">
            <p>Frontend Version:</p>
            <p>{{ FE_VERSION }}</p>
          </div>
        </div>
      </main>
    </div>
  </div>
</template>

<script setup lang="ts">
import IniaIcon from '@/components/atoms/IniaIcon/IniaIcon.vue'
import ThemeToggle from '@/components/atoms/ThemeToggle/ThemeToggle.vue'
import { computed, nextTick, ref, useId, useTemplateRef, watch, type Ref } from 'vue'
import { onKeyStroke } from '@vueuse/core'
import IniaWordmark from '@/components/atoms/IniaWordmark/IniaWordmark.vue'
import { RouterLink } from 'vue-router'
import { useI18n } from 'vue-i18n'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { useRouter } from 'vue-router'
import API from '@/api'
import { IniaSelect } from '@/components/molecules'
import { activeOrganizationId, clearSignIn, hasAnyRole, hasMultipleTenants } from '@/lib/utils'
import { extensionPoints, type DrawerLink, type MenuLink } from '@/modules/extensionPoints'
import { useMyReviewCounts } from '@/composables/useMyReviewCounts'
import { useCurrentUserRoles } from '@/composables/useCurrentUserRoles'
import { focusFirstConnected } from '@/utils/focus'
import { useFitsBetween } from '@/composables/useFitsBetween'
import { useCanReviewSignatures, useCapabilities } from '@/composables/useCapabilities'

const FE_VERSION = import.meta.env.VITE_VERSION
const { data: backendVersion } = API.Meta.useGetVersion()
const { mutateAsync: downloadLog } = API.Diagnostics.useDownloadLog()

// Only fetch tenants if user has multiple tenants (for dropdown)
const shouldFetchTenants = ref(hasMultipleTenants())
const { data: userTenants } = API.Users.useGetTenants(shouldFetchTenants)

const router = useRouter()

const sidebarOpen = ref(false)
const drawerOpen = ref(false)

const closePanels = () => {
  drawerOpen.value = false
  sidebarOpen.value = false
}

// The drawer and sidebar are plain divs, and a click on a part of either that takes no
// focus leaves focus on the page body, so only a window listener hears every Escape.
onKeyStroke('Escape', closePanels)

const menuFits = useFitsBetween(
  useTemplateRef<HTMLElement>('header'),
  useTemplateRef<HTMLElement>('headerStart'),
  useTemplateRef<HTMLElement>('headerMenu'),
  useTemplateRef<HTMLElement>('headerEnd'),
)

// One button opens the tools beside the full menu and the whole navigation in its place, so focus
// always has a button to come back to, whichever way the menu last went.
const openPanel = () => {
  if (menuFits.value) drawerOpen.value = !drawerOpen.value
  else sidebarOpen.value = true
}

const drawerTitleId = useId()

const moveFocusWithPanel = (open: Ref<boolean>, panel: Readonly<Ref<HTMLElement | null>>, trigger: Readonly<Ref<HTMLElement | null>>) =>
  watch(open, async isOpen => {
    await nextTick()
    focusFirstConnected(isOpen ? panel.value : trigger.value)
  })

const panelTrigger = useTemplateRef<HTMLElement>('panelTrigger')
moveFocusWithPanel(drawerOpen, useTemplateRef<HTMLElement>('drawer'), panelTrigger)
moveFocusWithPanel(sidebarOpen, useTemplateRef<HTMLElement>('sidebar'), panelTrigger)

const selectedTenant = ref<string | undefined>(activeOrganizationId())

const { t: $t } = useI18n()

const { isAdmin, isEditor, isOperator } = useCurrentUserRoles()

const has = useCapabilities()

const canSee = (link: MenuLink) => (!link.roles || hasAnyRole(link.roles)) && (!link.capability || has(link.capability))

const isGroupActive = (pathPrefix: string | undefined) =>
  !!pathPrefix && router.currentRoute.value.path.startsWith(pathPrefix)

const canAccessSignatures = useCanReviewSignatures()

// Count of fields awaiting a vote from the current user (reviewer pill, red)
// and counter-suggestions awaiting the current user's decision as proposer
// (amber pill), aggregated across every workflow type.
const {
  totalCount: signatureReviewCount,
  proposerTotalCount: signatureProposerActionCount,
} = useMyReviewCounts()

// Main nav items (shown in top bar)
const mainMenuItems = computed(() => {
  const items: { title: string; to: string; pathPrefix?: string; children?: { title: string; to: string }[] }[] = [
    {
      title: $t('shared.menu.home'),
      to: '/',
    },
    {
      title: $t('shared.menu.workflowReports'),
      to: '/workflow-reports',
    },
  ]

  if (canAccessSignatures.value) {
    items.push({
      title: $t('shared.menu.signatures'),
      to: '/signatures',
    })
  }

  for (const group of extensionPoints.topMenu) {
    const children = group.links.filter(canSee).map((link) => ({ title: $t(link.titleKey), to: link.to }))
    if (children.length > 0) {
      items.push({ title: $t(group.titleKey), to: children[0].to, pathPrefix: group.pathPrefix, children })
    }
  }

  items.push(
    {
      title: $t('shared.menu.documents'),
      to: '/documents',
    },
    {
      title: $t('shared.menu.counterpartyManagement'),
      to: '/counterparty-management',
    },
  )

  return items
})

// Hamburger menu items (shown in left drawer)
const hamburgerMenuItems = computed(() => {
  const items: { title: string; to: string; icon: string }[] = []
  const moduleLinks = (placement: DrawerLink['placement']) =>
    extensionPoints.drawerMenu
      .filter((link) => link.placement === placement && canSee(link))
      .map((link) => ({ title: $t(link.titleKey), to: link.to, icon: link.icon }))

  items.push(...moduleLinks('first'))

  if (isEditor.value) {
    items.push({
      title: $t('shared.menu.documentCategoryManagement'),
      to: '/document-category-management',
      icon: 'folder',
    })
    items.push({
      title: $t('shared.menu.counterpartyTypes'),
      to: '/counterparty-types',
      icon: 'tags',
    })
  }

  if (isOperator.value || isAdmin.value) {
    items.push({
      title: $t('shared.menu.tokenUsageAnalytics'),
      to: '/token-usage',
      icon: 'chart-line',
    })
  }

  if (isOperator.value) {
    items.push({
      title: $t('shared.menu.modelPricing'),
      to: '/model-pricing',
      icon: 'dollar-sign',
    })
  }

  items.push(...moduleLinks('last'))

  return items
})

// All menu items for the navigation sidebar (flatten dropdown children)
const menuItems = computed(() => {
  const items: { title: string; to: string }[] = []
  for (const item of mainMenuItems.value) {
    if (item.children) {
      items.push(...item.children)
    } else {
      items.push(item)
    }
  }
  items.push(...hamburgerMenuItems.value)
  return items
})

const tenantOptions = computed(() => {
  if (!userTenants.value) {
    return []
  }

  return userTenants.value?.map(tenant => {
    return {
      label: tenant.name,
      value: tenant.id,
    }
  })
})

const showTenantSelector = computed(() => {
  return tenantOptions.value?.length > 1
})

const onTenantChange = (value: string | undefined) => {
  if (value) {
    localStorage.setItem('selectedOrgId', value)
    window.location.reload()
  }
}

const onLogout = async () => {
  clearSignIn()
  router.push({ name: 'Login' })
}

const onProfile = () => {
  router.push({ name: 'Profile' })
}

const onLogoClick = () => {
  router.push({ name: 'Home' })
  sidebarOpen.value = false
}

const onDownloadLog = async () => {
  try {
    const blob = await downloadLog({})
    const url = window.URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = 'log.zip'
    document.body.appendChild(a)
    a.click()
    window.URL.revokeObjectURL(url)
    document.body.removeChild(a)
  } catch (error) {
    console.error('Failed to download log:', error)
  }
}
</script>

<style scoped>
/* ========================================
   LAYOUT CONTAINER
   ======================================== */
.layout-container {
  display: flex;
  height: 100vh;
  width: 100%;
  background: var(--bg-0);
}

.mobile-overlay {
  position: fixed;
  inset: 0;
  z-index: 20;
  background-color: rgba(0, 0, 0, 0.5);
  transition: opacity 0.3s;
}

.mobile-sidebar {
  position: fixed;
  top: 0;
  bottom: 0;
  left: 0;
  z-index: 30;
  width: 240px;
  overflow-y: auto;
  background: var(--nav-bg);
  color: var(--nav-ink);
  scrollbar-color: var(--nav-ink-muted) var(--nav-bg);
  border-right: 1px solid var(--nav-stroke);
  transition: transform 0.3s ease-in;
  transform: translateX(-100%);
}

.mobile-sidebar--open {
  transform: translateX(0);
  transition-timing-function: ease-out;
}

.sidebar-content {
  padding: var(--spacing-md) var(--spacing-sm);
}

.sidebar-logo {
  width: 87px;
  cursor: pointer;
  margin-bottom: var(--spacing-lg);
}

.logo-image {
  display: block;
  width: 100%;
}

.sidebar-nav {
  margin-top: var(--spacing-lg);
  display: flex;
  flex-direction: column;
  gap: var(--spacing-xs);
}

.nav-link {
  display: flex;
  align-items: center;
  padding: var(--spacing-xs) var(--spacing-sm);
  border-radius: var(--radius-small);
  color: var(--nav-ink-muted);
  transition: all 0.15s ease-in-out;
}

.nav-link:hover {
  color: var(--nav-ink);
  background: var(--nav-hover);
}

.nav-link-active {
  color: var(--nav-ink) !important;
  background: var(--nav-active);
  font-weight: 600;
}

.nav-text {
  font-size: 14px;
  margin-left: var(--spacing-sm);
}

/* ========================================
   MAIN CONTAINER
   ======================================== */
.main-container {
  display: flex;
  flex-direction: column;
  flex: 1;
  overflow: hidden;
}

/* ========================================
   HEADER
   ======================================== */
.header {
  display: grid;
  grid-template-columns: 1fr auto 1fr;
  align-items: center;
  column-gap: var(--spacing-md);
  padding: var(--spacing-sm) var(--spacing-md);
  background: var(--nav-bg);
  color: var(--nav-ink);
  border-bottom: 1px solid var(--nav-stroke);
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.1);
  position: relative;
}

.header-left {
  display: flex;
  align-items: center;
  gap: var(--spacing-sm);
  grid-column: 1;
  justify-self: start;
}

.header-logo {
  width: 54px;
  display: none;
  cursor: pointer;
}

@media (min-width: 768px) {
  .header-logo {
    display: block;
  }
}


.desktop-nav {
  grid-column: 2;
}

/* The items keep their own width while the menu collapses to none, so a collapsed menu is still
   measured, and still noticed when its items change. */
.desktop-nav--collapsed {
  position: absolute;
  width: 0;
  overflow: hidden;
  visibility: hidden;
}

.desktop-nav-items {
  display: flex;
  align-items: center;
  gap: var(--spacing-xs);
  width: max-content;
}

.desktop-nav-link {
  display: flex;
  align-items: center;
  padding: var(--spacing-xs) var(--spacing-sm);
  border-radius: var(--radius-small);
  color: var(--nav-ink-muted);
  font-size: 14px;
  transition: all 0.15s ease-in-out;
  position: relative;
}

.desktop-nav-link:hover {
  color: var(--nav-ink);
  background: var(--nav-hover);
}

.desktop-nav-link-active {
  color: var(--nav-ink) !important;
  font-weight: 600;
}

.desktop-nav-link-active::after {
  content: '';
  position: absolute;
  bottom: -1px;
  left: var(--spacing-sm);
  right: var(--spacing-sm);
  height: 2px;
  background: var(--nav-ink);
}

.desktop-nav-text {
  font-size: 14px;
}

/* Small red pill with the count of fields awaiting this user's review.
   Rendered next to the Signatures nav label when the aggregate count > 0. */
.desktop-nav-review-badge {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-width: 18px;
  height: 18px;
  padding: 0 0.4rem;
  margin-left: 0.5rem;
  border-radius: 999px;
  background: var(--accent-error-fill);
  color: #fff;
  font-size: 10px;
  font-weight: 700;
  letter-spacing: 0.03em;
  line-height: 1;
  font-variant-numeric: tabular-nums;
}
/* Amber variant for the proposer-side "counter-suggestion waiting for you"
   indicator, visually distinct from the red reviewer-needs-vote badge so the
   two roles don't get confused. */
.desktop-nav-review-badge--proposer {
  background: var(--accent-warning-fill);
  color: #1a3850;
}

.dark .desktop-nav-review-badge--proposer {
  background: var(--accent-warning-light);
  color: var(--accent-warning);
}

.nav-dropdown-icon {
  font-size: 10px;
  margin-left: 4px;
  color: var(--nav-ink-muted);
}

.hamburger-trigger {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 36px;
  height: 36px;
  border-radius: var(--radius-small);
  cursor: pointer;
  transition: all 0.15s ease-in-out;
  background: transparent;
  border: none;
}

.hamburger-trigger:hover {
  background: var(--nav-hover);
}

.hamburger-icon {
  font-size: 18px;
  color: var(--nav-ink-muted);
}

.drawer-overlay {
  position: fixed;
  inset: 0;
  z-index: 40;
  background: rgba(0, 0, 0, 0.5);
}

.desktop-drawer {
  position: fixed;
  top: 0;
  left: 0;
  bottom: 0;
  width: 260px;
  background: var(--bg-1);
  border-right: 1px solid var(--stroke);
  z-index: 50;
  transform: translateX(-100%);
  transition: transform 0.3s ease-in-out;
}

.desktop-drawer.drawer-open {
  transform: translateX(0);
  box-shadow: 4px 0 20px rgba(0, 0, 0, 0.3);
}

.drawer-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: var(--spacing-lg) var(--spacing-md);
  border-bottom: 1px solid var(--stroke);
}

.drawer-title {
  font-size: 18px;
  font-weight: 600;
  color: var(--text-primary);
}

.drawer-close {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 32px;
  height: 32px;
  border: none;
  background: transparent;
  color: var(--text-secondary);
  border-radius: var(--radius-small);
  cursor: pointer;
  transition: all 0.15s ease-in-out;
}

.drawer-close:hover {
  color: var(--text-primary);
  background: var(--nav-hover);
}

.drawer-nav {
  display: flex;
  flex-direction: column;
  padding: var(--spacing-md);
  gap: var(--spacing-xs);
}

.drawer-link {
  display: flex;
  align-items: center;
  gap: var(--spacing-sm);
  padding: var(--spacing-sm) var(--spacing-md);
  color: var(--text-secondary);
  font-size: 14px;
  border-radius: var(--radius-small);
  transition: all 0.15s ease-in-out;
}

.drawer-link:hover {
  color: var(--text-primary);
  background: var(--bg-2);
}

.drawer-link-active {
  color: var(--text-primary);
  background: linear-gradient(135deg, rgba(26, 56, 80, 0.15), rgba(26, 56, 80, 0.15));
  font-weight: 600;
}

.drawer-link-icon {
  font-size: 16px;
  width: 20px;
  text-align: center;
}

.header-actions {
  display: flex;
  align-items: center;
  gap: var(--spacing-sm);
  grid-column: 3;
  justify-self: end;
}

.tenant-selector {
  width: 110px;
  display: none;
}

/* Repainted for this one placement rather than changing IniaSelect, which
   sits on light surfaces everywhere else: like the ThemeToggle beside it, a
   white card with navy type on the strip in both themes. */
.tenant-selector :deep(button) {
  background: var(--bg-1);
  border-color: var(--stroke);
  color: var(--text-primary);
}

.tenant-selector :deep(button:hover) {
  background: var(--bg-2);
  border-color: var(--stroke-hover);
}

.tenant-selector :deep(button svg),
.tenant-selector :deep(button span) {
  color: var(--text-primary);
}

.dark .tenant-selector :deep(button) {
  background: #ffffff;
  border-color: var(--nav-stroke);
  color: var(--nav-ink);
}

.dark .tenant-selector :deep(button:hover) {
  background: var(--nav-hover);
  border-color: var(--nav-ink-muted);
}

.dark .tenant-selector :deep(button svg),
.dark .tenant-selector :deep(button span) {
  color: var(--nav-ink);
}

@media (min-width: 768px) {
  .tenant-selector {
    display: block;
  }
}

.download-log-btn {
  display: flex;
  align-items: center;
  gap: var(--spacing-xs);
  padding: 6px 12px;
  border-radius: var(--radius-small);
  cursor: pointer;
  transition: all 0.15s ease-in-out;
  color: var(--nav-ink-muted);
}

.download-log-btn:hover {
  color: var(--nav-ink);
  background: var(--nav-hover);
}

.download-icon {
  font-size: 16px;
  transition: color 0.15s ease-in-out;
}

.download-text {
  font-size: 12px;
  font-weight: 600;
  line-height: 1;
}

.user-menu-trigger {
  width: 32px;
  height: 32px;
  display: flex;
  align-items: center;
  justify-content: center;
  border-radius: 50%;
  /* Nav-scoped: the page tokens track the page, not the strip, and vanish
     against the counter-inverted strip in both themes. */
  border: 2px solid var(--nav-ink-muted);
  color: var(--nav-ink);
  cursor: pointer;
  transition: all 0.15s ease-in-out;
}

.user-menu-trigger:hover {
  border-color: var(--nav-ink);
  background: var(--nav-hover);
}

.user-icon {
  font-size: 18px;
  color: var(--nav-ink);
}

.user-menu-content {
  margin-right: var(--spacing-sm);
}

/* ========================================
   MAIN CONTENT
   ======================================== */
.main-content {
  flex: 1;
  overflow-x: hidden;
  overflow-y: auto;
  background: var(--bg-0);
}

.version-info {
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  font-size: 11px;
  color: var(--text-muted);
  padding: var(--spacing-md);
  gap: var(--spacing-xs);
}

.version-item {
  display: flex;
  gap: 4px;
}

/* Scrollbar */
.main-content::-webkit-scrollbar {
  width: 6px;
}

.main-content::-webkit-scrollbar-track {
  background: transparent;
}

.main-content::-webkit-scrollbar-thumb {
  background: rgba(255, 255, 255, 0.1);
  border-radius: 3px;
}

.main-content::-webkit-scrollbar-thumb:hover {
  background: rgba(255, 255, 255, 0.15);
}
</style>
