import { Modal } from "antd"
import AppButton from "./AppButton"
import { useState } from "react"

function AreYouSureModal(props) {



    function buttonSelected(action) {
        action(props.context)
    }

    return (
        <Modal footer={[]} title={props.title} open={props.show} onCancel={() => { buttonSelected(props.onNoClicked) }}>

            <label>{props.description}</label>

            <div style={{display: 'flex', justifyContent: 'flex-end'}}>

                <AppButton onClick={() => { buttonSelected(props.onYesClicked) }}>
                    Yes
                </AppButton>
                <AppButton onClick={() => { buttonSelected(props.onNoClicked) }}>
                    No
                </AppButton>

            </div>
        </Modal>
    )

}

export default AreYouSureModal