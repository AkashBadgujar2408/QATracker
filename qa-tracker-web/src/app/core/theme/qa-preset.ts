import { definePreset } from '@primeuix/themes';
import Aura from '@primeuix/themes/aura';

/** PrimeNG preset aligned with the design tokens (plan 10.2): indigo primary, slate surfaces, 6 px control radius. */
export const QaPreset = definePreset(Aura, {
  primitive: {
    borderRadius: {
      sm: '4px',
      md: '6px',
      lg: '10px',
    },
  },
  semantic: {
    primary: {
      50: '{indigo.50}',
      100: '{indigo.100}',
      200: '{indigo.200}',
      300: '{indigo.300}',
      400: '{indigo.400}',
      500: '{indigo.500}',
      600: '{indigo.600}',
      700: '{indigo.700}',
      800: '{indigo.800}',
      900: '{indigo.900}',
      950: '{indigo.950}',
    },
    colorScheme: {
      light: {
        primary: {
          color: '{indigo.600}',
          contrastColor: '#ffffff',
          hoverColor: '{indigo.700}',
          activeColor: '{indigo.800}',
        },
        surface: {
          0: '#ffffff',
          50: '{slate.50}',
          100: '{slate.100}',
          200: '{slate.200}',
          300: '{slate.300}',
          400: '{slate.400}',
          500: '{slate.500}',
          600: '{slate.600}',
          700: '{slate.700}',
          800: '{slate.800}',
          900: '{slate.900}',
          950: '{slate.950}',
        },
      },
      dark: {
        primary: {
          color: '{indigo.400}',
          contrastColor: '{slate.950}',
          hoverColor: '{indigo.300}',
          activeColor: '{indigo.200}',
        },
        surface: {
          0: '#ffffff',
          50: '{slate.50}',
          100: '{slate.100}',
          200: '{slate.200}',
          300: '{slate.300}',
          400: '{slate.400}',
          500: '{slate.500}',
          600: '{slate.600}',
          700: '{slate.700}',
          800: '{slate.800}',
          900: '{slate.900}',
          950: '{slate.950}',
        },
      },
    },
  },
});
