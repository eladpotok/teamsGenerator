import { Button } from 'antd'
import './AppButton.css'

function AppButton(props) {

        const myClass = props.secondary ? 'secondary' : 'button'

        return (
            <button  class={myClass} onClick={props.onClick}>
                {props.children}
            </button>
        )

}

export default AppButton