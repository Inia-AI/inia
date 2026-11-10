import { createRouter, createWebHistory } from 'vue-router'
import { clearSignIn, isTokenValid, hasAnyRole, hasOrganization } from '../../lib/utils'
import { withReportsListExtensions } from '../../modules/extensionPoints'
import { hasCapability } from '../../lib/capabilityCheck'
import { CAPABILITIES } from '../../constants/capabilities'
import {
  ADMIN_AND_OPERATOR_ROLES,
  EDITOR_ROLES,
  OPERATOR_ROLES,
  ROLES,
} from '../../constants/roles'

declare module 'vue-router' {
  interface RouteMeta {
    layout?: 'blank'
    isPublic?: boolean
    allowedRoles?: readonly string[]
    /** Without this capability the address explains that the account does not include it. */
    capability?: string
    /** The i18n key of the page's name in the browser tab. */
    title?: string
  }
}

// Lazy-loaded route components for optimal code-splitting
const HomeView = () => import('../../views/HomeView.vue')
const LoginView = () => import('../../views/LoginView.vue')
const TwoFactorVerifyView = () => import('../../views/TwoFactorVerifyView.vue')
const WorkflowReports = () => withReportsListExtensions(import('../../views/WorkflowReports.vue'))
const WorkflowExecutionLogs = () => import('../../views/WorkflowExecutionLogs.vue')
const WorkflowDetails = () => import('../../views/WorkflowDetails.vue')
const CreateWorkflow = () => import('../../views/CreateWorkflow.vue')
const ForgotPassword = () => import('../../views/ForgotPassword.vue')
const ResetPassword = () => import('../../views/ResetPassword.vue')
const Health = () => import('../../views/HealthView.vue')
const ProfileView = () => import('../../views/ProfileView.vue')
const CounterpartyManagement = () => import('../../views/CounterpartyManagement.vue')
const CounterpartyTypeManagement = () => import('../../views/CounterpartyTypeManagement.vue')
const DocumentCategoryManagement = () => import('../../views/DocumentCategoryManagement.vue')
const TokenUsageAnalytics = () => import('../../views/TokenUsageAnalytics.vue')
const ModelPricingManagement = () => import('../../views/ModelPricingManagement.vue')
const Documents = () => import('../../views/DocumentsList.vue')
const DocumentDetails = () => import('../../views/DocumentDetails.vue')
const SignatureSettings = () => import('../../views/SignatureSettings.vue')
const NotFoundView = () => import('../../views/NotFoundView.vue')
const NoOrganizationView = () => import('../../views/NoOrganizationView.vue')
const NotIncludedView = () => import('../../views/NotIncludedView.vue')

export const UNKNOWN_ADDRESS = 'UnknownAddress'
export const NO_ORGANIZATION = 'NoOrganization'
export const NOT_INCLUDED = 'NotIncluded'

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes: [
    {
      path: '/',
      name: 'Home',
      component: HomeView,
      meta: { title: 'shared.menu.home' },
    },
    {
      path: '/workflow-reports',
      name: 'WorkflowReports',
      component: WorkflowReports,
      meta: { title: 'page.financialAnalysis.title' },
    },
    {
      path: '/workflow-reports/:id/logs',
      name: 'ExecutionLogs',
      component: WorkflowExecutionLogs,
      meta: { title: 'page.workflowExecutionLogs.title', capability: CAPABILITIES.RUN_LOGS },
    },
    {
      path: '/workflows/create',
      name: 'CreateWorkflow',
      component: CreateWorkflow,
      meta: { title: 'page.createWorkflow.title', capability: CAPABILITIES.RUN },
    },
    {
      path: '/workflow-reports/:id/:action',
      name: 'WorkflowDetails',
      component: WorkflowDetails,
      meta: { title: 'page.workflowDetails.title' },
    },
    {
      path: '/login',
      name: 'Login',
      component: LoginView,
      meta: { title: 'page.login.documentTitle', layout: 'blank', isPublic: true },
    },
    {
      path: '/forgot-password',
      name: 'ForgotPassword',
      component: ForgotPassword,
      meta: { title: 'page.forgotPassword.title', layout: 'blank', isPublic: true },
    },
    {
      path: '/reset-password',
      name: 'ResetPassword',
      component: ResetPassword,
      meta: { title: 'page.resetPassword.title', layout: 'blank', isPublic: true },
    },
    {
      path: '/2fa-verify',
      name: 'TwoFactorVerify',
      component: TwoFactorVerifyView,
      meta: { title: 'page.twoFactor.title', layout: 'blank', isPublic: true },
    },
    {
      path: '/health',
      name: 'Health',
      component: Health,
      meta: {
        title: 'page.health.title',
        layout: 'blank',
        isPublic: true,
      },
    },
    {
      path: '/profile',
      name: 'Profile',
      component: ProfileView,
      meta: { title: 'page.profile.title' },
    },
    {
      path: '/counterparty-management',
      name: 'CounterpartyManagement',
      component: CounterpartyManagement,
      meta: { title: 'page.counterpartyManagement.title' },
    },
    {
      path: '/counterparty-types',
      name: 'CounterpartyTypeManagement',
      component: CounterpartyTypeManagement,
      meta: { title: 'page.counterpartyManagement.types.title', allowedRoles: EDITOR_ROLES },
    },
    {
      path: '/documents',
      name: 'Documents',
      component: Documents,
      meta: { title: 'page.documents.title' },
    },
    {
      path: '/documents/:id',
      name: 'DocumentDetails',
      component: DocumentDetails,
      meta: { title: 'page.documents.detail.title' },
    },
    {
      path: '/document-category-management',
      name: 'DocumentCategoryManagement',
      component: DocumentCategoryManagement,
      meta: { title: 'page.documentCategoryManagement.title', allowedRoles: EDITOR_ROLES },
    },
    {
      path: '/token-usage',
      name: 'TokenUsageAnalytics',
      component: TokenUsageAnalytics,
      meta: { title: 'shared.menu.tokenUsageAnalytics', allowedRoles: ADMIN_AND_OPERATOR_ROLES },
    },
    {
      path: '/model-pricing',
      name: 'ModelPricingManagement',
      component: ModelPricingManagement,
      meta: { title: 'page.modelPricingManagement.title', allowedRoles: OPERATOR_ROLES },
    },
    {
      path: '/signatures',
      name: 'SignaturesPicker',
      component: SignatureSettings,
      meta: {
        title: 'page.signatures.title',
        allowedRoles: [ROLES.SUPER_ADMIN, ROLES.SIGNATURE_CONTRIBUTOR],
        capability: CAPABILITIES.SIGNATURES,
      },
    },
    {
      path: '/signatures/:workflowTypeId',
      name: 'SignatureSettings',
      component: SignatureSettings,
      meta: {
        title: 'page.signatures.title',
        allowedRoles: [ROLES.SUPER_ADMIN, ROLES.SIGNATURE_CONTRIBUTOR],
        capability: CAPABILITIES.SIGNATURES,
      },
    },
    {
      path: '/no-organization',
      name: NO_ORGANIZATION,
      component: NoOrganizationView,
      meta: { title: 'page.noOrganization.title', layout: 'blank' },
    },
    {
      path: '/:unknownAddress(.*)*',
      name: UNKNOWN_ADDRESS,
      component: NotFoundView,
      meta: { title: 'page.notFound.title' },
    },
    // Reached only by name, so the address bar keeps the address the account does not include; the catch-all above wins every match by address.
    {
      path: '/:notIncludedAddress(.*)*',
      name: NOT_INCLUDED,
      component: NotIncludedView,
      meta: { title: 'page.notIncluded.title' },
    },
  ],
})

router.beforeEach(async (to) => {
  if (to.meta.isPublic) {
    return
  }
  if (!isTokenValid()) {
    clearSignIn()
    return '/login?redirect=' + to.fullPath
  }
  if (!hasOrganization()) {
    return to.name === NO_ORGANIZATION ? undefined : { name: NO_ORGANIZATION }
  }
  if (to.name === NO_ORGANIZATION) {
    return '/'
  }
  if (to.meta.allowedRoles && !hasAnyRole(to.meta.allowedRoles)) {
    return '/'
  }
  if (to.meta.capability && !(await hasCapability(to.meta.capability))) {
    return {
      name: NOT_INCLUDED,
      params: { notIncludedAddress: to.path.substring(1).split('/').map(decodeURIComponent) },
      query: to.query,
      hash: to.hash,
      state: { capability: to.meta.capability },
    }
  }
})

export default router
