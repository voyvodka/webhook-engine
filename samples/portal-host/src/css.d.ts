// Vite handles CSS imports at bundle time; this declaration tells TypeScript
// that side-effect CSS imports are valid without emitting any module shape.
declare module "*.css" {
  const _: string;
  export default _;
}
