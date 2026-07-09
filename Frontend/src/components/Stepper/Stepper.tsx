import React from 'react'
import './Stepper.css'

interface StepperProps {
  steps: string[]
  activeStep: number
  onChangeStep?: (stepIndex: number) => void
}

export const Stepper: React.FC<StepperProps> = ({
  steps,
  activeStep,
  onChangeStep
}) => {
  return (
    <div className="stepper-container">
      {steps.map((label, index) => {
        const isActive = index === activeStep
        const isCompleted = index < activeStep

        const handleStepClick = () => {
          if (onChangeStep) {
            onChangeStep(index)
          }
        }

        return (
          <button
            key={index}
            type="button"
            className={`stepper-step ${isActive ? 'active' : ''} ${isCompleted ? 'completed' : ''}`}
            onClick={handleStepClick}
            disabled={!onChangeStep}
          >
            <span>{label}</span>
          </button>
        )
      })}
    </div>
  )
}
export default Stepper
